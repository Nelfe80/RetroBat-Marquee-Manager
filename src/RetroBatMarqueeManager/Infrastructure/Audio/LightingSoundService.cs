using System.Diagnostics;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace RetroBatMarqueeManager.Infrastructure.Audio;

/// <summary>
/// Audio side of the lighting engine (CDC §24): light and sound are driven by the
/// same tube state, so sound level and flicker are strictly synchronous.
/// Two loops (hum follows the lit level, buzz follows electrical instability) plus
/// short one-shots for calibrated discrete events (strike ticks, ignition, pop).
/// Master volume is hard-capped at 30%. Fully non-fatal: any audio failure
/// disables the service and the lighting keeps rendering.
/// </summary>
public sealed class LightingSoundService : IDisposable
{
    public const float MasterCap = 0.30f;
    private const int MaxSimultaneousOneShots = 4;
    private const int MinOneShotIntervalMs = 70;

    private readonly ILogger _logger;
    private readonly string _directory;
    private readonly float _master;
    private readonly List<IDisposable> _ownedReaders = new();
    private readonly Dictionary<string, long> _lastOneShotAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private static readonly string[] PreloadedOneShots =
        { "ampoule-on-off.mp3", "ampoule-on.mp3", "neon-on.mp3", "neon-pop.mp3" };

    /// <summary>Long non-looped sequences whose amplitude envelope can drive the light.</summary>
    public static readonly string[] SequenceFiles =
        { "flickering-neon.mp3", "neon-sign-flicker-and-electric-buzz.mp3", "light_neon_dysfunction.mp3" };

    private const int EnvelopeHz = 50;

    private readonly Dictionary<string, float[]> _oneShotCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (float[] Envelope, double Duration)> _envelopeCache = new(StringComparer.OrdinalIgnoreCase);
    private WaveOutEvent? _output;
    private MixingSampleProvider? _mixer;
    private VolumeSampleProvider? _hum;
    private VolumeSampleProvider? _buzz;
    private int _activeOneShots;
    private float _humLevel;
    private float _buzzLevel;
    private volatile bool _ready;

    public LightingSoundService(ILogger logger, string soundsDirectory, float masterVolume)
    {
        _logger = logger;
        _directory = soundsDirectory;
        _master = Math.Clamp(masterVolume, 0f, MasterCap);
    }

    public void Start()
    {
        try
        {
            if (!Directory.Exists(_directory))
            {
                _logger.LogWarning("Lighting sounds directory not found, audio disabled: {Dir}", _directory);
                return;
            }
            _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2)) { ReadFully = true };
            _hum = AddLoop("neon.mp3");
            _buzz = AddLoop("neonlight_highpitc.mp3");
            // short event sounds are fully decoded up front: zero-latency, reliable
            // even in rapid ignition sequences (no MediaFoundation reader per tick)
            foreach (var file in PreloadedOneShots) PreloadOneShot(file);
            // amplitude envelopes of the long sequences, computed off-thread
            Task.Run(PrecomputeEnvelopes);
            _output = new WaveOutEvent { DesiredLatency = 120 };
            _output.Init(_mixer);
            _output.Play();
            _ready = true;
            _logger.LogInformation("Lighting sound service started (master volume {Master:P0})", _master);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lighting sound service failed to start; audio disabled");
            _ready = false;
        }
    }

    /// <summary>
    /// Called from the render thread each rendered frame, right after the tube
    /// simulators are updated — the loops track the visuals sample-accurately at
    /// the flicker cadence. Values 0..1, scaled by the capped master volume.
    /// </summary>
    public void SetLevels(float hum, float buzz)
    {
        if (!_ready) return;
        // light smoothing avoids zipper noise on 24 Hz volume steps
        _humLevel += (Math.Clamp(hum, 0f, 1f) - _humLevel) * 0.5f;
        _buzzLevel += (Math.Clamp(buzz, 0f, 1f) - _buzzLevel) * 0.6f;
        if (_hum != null) _hum.Volume = _humLevel * _master;
        if (_buzz != null) _buzz.Volume = _buzzLevel * _master;
    }

    /// <summary>Short calibrated event sound, played from the in-memory cache.
    /// Rate-limited per file; capped concurrency.</summary>
    public void PlayOneShot(string fileName, float volume)
    {
        if (!_ready || _mixer == null) return;
        try
        {
            var now = _clock.ElapsedMilliseconds;
            lock (_lastOneShotAt)
            {
                if (_lastOneShotAt.TryGetValue(fileName, out var last) && now - last < MinOneShotIntervalMs) return;
                _lastOneShotAt[fileName] = now;
            }
            if (Volatile.Read(ref _activeOneShots) >= MaxSimultaneousOneShots) return;
            if (!_oneShotCache.TryGetValue(fileName, out var data))
            {
                PreloadOneShot(fileName);
                if (!_oneShotCache.TryGetValue(fileName, out data)) return;
            }

            Interlocked.Increment(ref _activeOneShots);
            _mixer.AddMixerInput(new CachedSoundProvider(data, Math.Clamp(volume, 0f, 1f) * _master,
                _mixer.WaveFormat, () => Interlocked.Decrement(ref _activeOneShots)));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Lighting one-shot {File} failed", fileName);
        }
    }

    /// <summary>
    /// A long sound played once (never looped) whose amplitude envelope is exposed
    /// so the renderer can drive the tube intensity in true sync with what is heard.
    /// </summary>
    public sealed class SequenceHandle
    {
        private readonly float[] _envelope;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        internal volatile bool Killed;
        internal volatile bool Ended;

        internal SequenceHandle(float[] envelope, double duration)
        {
            _envelope = envelope;
            Duration = duration;
        }

        public double Duration { get; }
        public double Elapsed => _clock.Elapsed.TotalSeconds;
        public bool Done => Ended || Killed || Elapsed >= Duration;

        /// <summary>Amplitude 0..1 at the current playback instant.</summary>
        public float Level
        {
            get
            {
                if (Done || _envelope.Length == 0) return 0f;
                var index = (int)(Elapsed * EnvelopeHz);
                return index < _envelope.Length ? _envelope[index] : 0f;
            }
        }
    }

    /// <summary>Start a light-driving sequence; null if audio or envelope unavailable.</summary>
    public SequenceHandle? PlaySequence(string fileName, float volume)
    {
        if (!_ready || _mixer == null) return null;
        try
        {
            (float[] Envelope, double Duration) cached;
            lock (_envelopeCache)
            {
                if (!_envelopeCache.TryGetValue(fileName, out cached)) return null;
            }
            var path = Path.Combine(_directory, fileName);
            if (!File.Exists(path)) return null;

            var handle = new SequenceHandle(cached.Envelope, cached.Duration);
            var reader = new AudioFileReader(path) { Volume = Math.Clamp(volume, 0f, 1f) * _master };
            _mixer.AddMixerInput(Fit(new SequenceProvider(reader, handle)));
            return handle;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Lighting sequence {File} failed", fileName);
            return null;
        }
    }

    public void StopSequence(SequenceHandle handle) => handle.Killed = true;

    private void PrecomputeEnvelopes()
    {
        foreach (var file in SequenceFiles)
        {
            try
            {
                var path = Path.Combine(_directory, file);
                if (!File.Exists(path)) continue;
                using var reader = new AudioFileReader(path);
                var samplesPerWindow = reader.WaveFormat.SampleRate * reader.WaveFormat.Channels / EnvelopeHz;
                var envelope = new List<float>(2048);
                var buffer = new float[samplesPerWindow];
                int read;
                var peak = 0f;
                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    var max = 0f;
                    for (var i = 0; i < read; i++)
                    {
                        var value = Math.Abs(buffer[i]);
                        if (value > max) max = value;
                    }
                    envelope.Add(max);
                    if (max > peak) peak = max;
                }
                if (peak > 0)
                    for (var i = 0; i < envelope.Count; i++) envelope[i] /= peak;
                lock (_envelopeCache)
                {
                    _envelopeCache[file] = (envelope.ToArray(), envelope.Count / (double)EnvelopeHz);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cannot compute envelope for lighting sequence {File}", file);
            }
        }
        _logger.LogInformation("Lighting sequence envelopes ready: {Count} file(s)", _envelopeCache.Count);
    }

    private sealed class SequenceProvider : ISampleProvider
    {
        private readonly AudioFileReader _reader;
        private readonly SequenceHandle _handle;
        private bool _disposed;

        public SequenceProvider(AudioFileReader reader, SequenceHandle handle)
        {
            _reader = reader;
            _handle = handle;
        }

        public WaveFormat WaveFormat => _reader.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            if (_disposed) return 0;
            if (_handle.Killed) { End(); return 0; }
            var read = _reader.Read(buffer, offset, count);
            if (read == 0) End();
            return read;
        }

        private void End()
        {
            _disposed = true;
            _handle.Ended = true;
            _reader.Dispose();
        }
    }

    /// <summary>Decode a short sound entirely into the mixer format (44.1 kHz stereo float).</summary>
    private void PreloadOneShot(string fileName)
    {
        try
        {
            var path = Path.Combine(_directory, fileName);
            if (!File.Exists(path) || _oneShotCache.ContainsKey(fileName)) return;
            using var reader = new AudioFileReader(path);
            var source = Fit(reader);
            var samples = new List<float>(44100);
            var buffer = new float[4410];
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                for (var i = 0; i < read; i++) samples.Add(buffer[i]);
            _oneShotCache[fileName] = samples.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cannot preload lighting sound {File}", fileName);
        }
    }

    private VolumeSampleProvider? AddLoop(string fileName)
    {
        var path = Path.Combine(_directory, fileName);
        if (!File.Exists(path))
        {
            _logger.LogWarning("Lighting loop sound missing: {Path}", path);
            return null;
        }
        var reader = new AudioFileReader(path);
        _ownedReaders.Add(reader);
        var volume = new VolumeSampleProvider(Fit(new LoopingProvider(reader))) { Volume = 0f };
        _mixer!.AddMixerInput(volume);
        return volume;
    }

    /// <summary>Match any source to the mixer format (44.1 kHz stereo float).</summary>
    private static ISampleProvider Fit(ISampleProvider source)
    {
        if (source.WaveFormat.SampleRate != 44100)
            source = new WdlResamplingSampleProvider(source, 44100);
        if (source.WaveFormat.Channels == 1)
            source = new MonoToStereoSampleProvider(source);
        return source;
    }

    private sealed class LoopingProvider : ISampleProvider
    {
        private readonly AudioFileReader _reader;
        public LoopingProvider(AudioFileReader reader) => _reader = reader;
        public WaveFormat WaveFormat => _reader.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            var total = 0;
            while (total < count)
            {
                var read = _reader.Read(buffer, offset + total, count - total);
                if (read == 0)
                {
                    if (_reader.Length == 0) break;
                    _reader.Position = 0;
                    continue;
                }
                total += read;
            }
            return total;
        }
    }

    private sealed class CachedSoundProvider : ISampleProvider
    {
        private readonly float[] _data;
        private readonly float _volume;
        private readonly Action _onEnded;
        private int _position;
        private bool _ended;

        public CachedSoundProvider(float[] data, float volume, WaveFormat format, Action onEnded)
        {
            _data = data;
            _volume = volume;
            WaveFormat = format;
            _onEnded = onEnded;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            if (_ended) return 0;
            var available = Math.Min(count, _data.Length - _position);
            for (var i = 0; i < available; i++)
                buffer[offset + i] = _data[_position + i] * _volume;
            _position += available;
            if (available == 0)
            {
                _ended = true;
                _onEnded();
            }
            return available;
        }
    }

    public void Dispose()
    {
        _ready = false;
        try
        {
            _output?.Stop();
            _output?.Dispose();
        }
        catch { /* audio teardown must never take the app down */ }
        foreach (var reader in _ownedReaders) reader.Dispose();
        _ownedReaders.Clear();
    }
}
