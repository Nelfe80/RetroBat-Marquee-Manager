namespace RetroBatMarqueeManager.Application.Lighting;

/// <summary>
/// Life of a single tube: a randomized ignition scenario, then continuous life
/// events — dips, blinks, brown-outs, restrikes and the occasional definitive
/// death — so no two power-ons or minutes of operation ever look the same
/// (CDC §12.3 lamp states: rampingOn, on, flickering, unstable, afterglow, off).
/// LED tubes ramp fast and then live a perfectly clean life (modern repro, §2.3).
/// All state is driven from the render thread via <see cref="Update"/>.
/// </summary>
public sealed class TubeLifeSimulator
{
    private enum Phase { Waiting, Igniting, Steady, Dip, FlashOff, SagDown, SagHold, SagUp, RestrikeOff, Distress, Dead }
    private enum IgnitionStyle { Instant, Erratic, Warmup, Stubborn }

    private const double MeanSecondsBetweenEvents = 20.0;

    private readonly Random _rng;
    private readonly BulbProfile _bulb;
    private readonly bool _led;
    private readonly Func<bool> _mayDie;
    private readonly double _startDelay;
    private readonly int _seed;
    private readonly double _slotRate;   // ignition attempt rate, varies per tube

    private Phase _phase = Phase.Waiting;
    private double _phaseStart;
    private double _phaseDuration;
    private double _param;               // event parameter: dip depth, sag level…
    private IgnitionStyle _style;
    private double _nextEventAt = double.MaxValue;

    public enum SoundCue { None, Strike, Ignited, Popped }

    public double Intensity { get; private set; }
    public double Electrode { get; private set; }

    /// <summary>
    /// Electrical instability 0..1, the audio side of the same state: drives the
    /// buzz loop volume so sound level and flicker are strictly synchronous.
    /// </summary>
    public double Instability { get; private set; }

    public bool IsDead => _phase == Phase.Dead;

    private SoundCue _pendingCue;
    private bool _wasLit;

    /// <summary>Discrete unlit→lit transition: one short sound per calibrated flicker.</summary>
    private void TrackStrike(bool lit)
    {
        if (lit && !_wasLit && _pendingCue == SoundCue.None) _pendingCue = SoundCue.Strike;
        _wasLit = lit;
    }

    /// <summary>One-shot sound event raised by the last Update; consuming resets it.</summary>
    public SoundCue TakeCue()
    {
        var cue = _pendingCue;
        _pendingCue = SoundCue.None;
        return cue;
    }

    /// <param name="mayDie">Consulted when a distress event resolves: return false to
    /// veto a definitive death (e.g. the other tube of the pair is already dead).</param>
    public TubeLifeSimulator(BulbProfile bulb, Random rng, double startDelay, Func<bool> mayDie)
    {
        _rng = rng;
        _bulb = bulb;
        _led = bulb.SolidState;
        _mayDie = mayDie;
        _startDelay = startDelay;
        _seed = rng.Next(1 << 20);
        _slotRate = 18 + rng.NextDouble() * 16;
        _style = PickIgnitionStyle();
        _phaseDuration = IgnitionDuration(_style) * (_led ? 1.0 : bulb.IgnitionScale);
    }

    private IgnitionStyle PickIgnitionStyle()
    {
        var r = _rng.NextDouble();
        return _bulb.Technology switch
        {
            _ when _bulb.SolidState => IgnitionStyle.Instant,
            // filaments warm up, they never strike
            BulbTechnology.Incandescent => r < 0.30 ? IgnitionStyle.Instant : IgnitionStyle.Warmup,
            // neon signs almost always struggle before holding
            BulbTechnology.Neon => r < 0.55 ? IgnitionStyle.Erratic
                : r < 0.80 ? IgnitionStyle.Stubborn : IgnitionStyle.Warmup,
            _ => r < 0.22 ? IgnitionStyle.Instant     // snaps straight on
                : r < 0.62 ? IgnitionStyle.Erratic    // classic struggling start
                : r < 0.85 ? IgnitionStyle.Warmup     // progressive glow to full
                : IgnitionStyle.Stubborn              // long fight before igniting
        };
    }

    private double IgnitionDuration(IgnitionStyle style) => style switch
    {
        _ when _led => 0.12,
        IgnitionStyle.Instant => 0.06 + _rng.NextDouble() * 0.15,
        IgnitionStyle.Erratic => 0.80 + _rng.NextDouble() * 1.40,
        // library warmupSeconds drives the progressive glow when provided
        IgnitionStyle.Warmup => _bulb.WarmupSeconds > 0
            ? _bulb.WarmupSeconds * (0.7 + _rng.NextDouble() * 0.6)
            : 1.50 + _rng.NextDouble() * 1.50,
        _ => 2.40 + _rng.NextDouble() * 1.60
    };

    /// <summary>Advance to scene time <paramref name="t"/> (seconds since scene ready).</summary>
    public void Update(double t)
    {
        switch (_phase)
        {
            case Phase.Waiting:
                Intensity = 0;
                Electrode = 0;
                Instability = 0;
                if (t >= _startDelay) Enter(Phase.Igniting, t, _phaseDuration);
                break;

            case Phase.Igniting:
                RunIgnition(t);
                break;

            case Phase.Steady:
                if (t >= _nextEventAt) { StartEvent(t); break; }
                Intensity = 1.0 + _bulb.FlickerAmount * (Hash(Slot(t, 24) * 7 + _seed) - 0.5);
                Electrode = 0;
                Instability = _bulb.EventRateScale <= 0 ? 0 : 0.06;
                break;

            case Phase.Dip:
            {
                var p = Progress(t);
                Intensity = 1 - (1 - _param) * Math.Sin(Math.PI * p);
                Instability = Math.Clamp((1 - Intensity) * 1.2, 0, 1);
                if (p >= 1) EnterSteady(t);
                break;
            }

            case Phase.FlashOff:
                Intensity = 0.02;
                Electrode = 0.25;
                Instability = 0.9;
                if (Progress(t) >= 1)
                {
                    EnterSteady(t);
                    // sometimes the blink stutters: another one right away
                    if (_rng.NextDouble() < 0.35) _nextEventAt = t + 0.15 + _rng.NextDouble() * 0.3;
                }
                break;

            case Phase.SagDown:
            {
                var p = Progress(t);
                Intensity = 1 + (_param - 1) * Smooth(p) + 0.02 * (Hash(Slot(t, 24) + _seed) - 0.5);
                Instability = Math.Clamp((1 - Intensity) * 0.9, 0, 1);
                if (p >= 1) Enter(Phase.SagHold, t, 1.0 + _rng.NextDouble() * 3.0);
                break;
            }

            case Phase.SagHold:
                Intensity = _param + 0.03 * (Hash(Slot(t, 24) * 3 + _seed) - 0.5);
                Instability = Math.Clamp((1 - _param) * 0.9, 0, 1);
                if (Progress(t) >= 1) Enter(Phase.SagUp, t, 0.5 + _rng.NextDouble() * 1.0);
                break;

            case Phase.SagUp:
            {
                var p = Progress(t);
                Intensity = _param + (1 - _param) * Smooth(p);
                Instability = Math.Clamp((1 - Intensity) * 0.9, 0, 1);
                if (p >= 1) EnterSteady(t);
                break;
            }

            case Phase.RestrikeOff:
                Intensity = 0.02;
                Electrode = 0.35 + 0.2 * Hash(Slot(t, 20) + _seed);
                Instability = 0.8;
                if (Progress(t) >= 1)
                {
                    _style = IgnitionStyle.Erratic;
                    Enter(Phase.Igniting, t, 0.5 + _rng.NextDouble() * 0.7);
                }
                break;

            case Phase.Distress:
            {
                // heavy terminal flicker, then the pop
                var slot = Slot(t, 30);
                var lit = Hash(slot * 13 + _seed) < 0.70;
                Intensity = lit ? 0.55 + 0.50 * Hash(slot * 5 + _seed) : 0.08;
                Electrode = lit ? 0.10 : 0.30;
                Instability = lit ? 0.55 : 0.95;
                TrackStrike(lit);
                if (Progress(t) >= 1)
                {
                    _phase = Phase.Dead;
                    Intensity = 0;
                    Electrode = 0;
                    Instability = 0;
                    _pendingCue = SoundCue.Popped;
                }
                break;
            }

            case Phase.Dead:
                Intensity = 0;
                Electrode = 0;
                Instability = 0;
                break;
        }
    }

    private void RunIgnition(double t)
    {
        var p = Progress(t);
        if (p >= 1) { EnterSteady(t, announce: true); Update(t); return; }
        var e = t - _phaseStart;
        var slot = (int)(e * _slotRate);

        switch (_style)
        {
            case IgnitionStyle.Instant:
                Intensity = p;
                Electrode = 0;
                Instability = _led ? 0 : 0.25 * (1 - p);
                break;

            case IgnitionStyle.Warmup:
                Intensity = 0.12 + 0.88 * Math.Pow(p, 1.6) * (1 + 0.05 * (Hash(slot + _seed) - 0.5));
                Electrode = (1 - p) * 0.30;
                Instability = 0.35 * (1 - p) + 0.10;
                break;

            default: // Erratic / Stubborn
                var pLit = _style == IgnitionStyle.Erratic
                    ? 0.08 + 0.90 * p * p
                    : 0.05 + 0.85 * p * p * p;
                var lit = Hash(slot * 31 + _seed) < pLit;
                Intensity = lit ? 0.75 + 0.30 * Hash(slot * 7 + _seed) : 0.02 + 0.05 * Hash(slot * 13 + _seed);
                Electrode = lit ? 0.10 : 0.40 + 0.35 * Hash(slot * 17 + _seed);
                Instability = lit ? 0.30 : 0.90;   // buzz gated exactly on the dark slots
                TrackStrike(lit);
                break;
        }
    }

    private void StartEvent(double t)
    {
        var r = _rng.NextDouble();
        if (r < 0.35)
        {
            _param = 0.55 + _rng.NextDouble() * 0.30;
            Enter(Phase.Dip, t, 0.15 + _rng.NextDouble() * 0.35);
        }
        else if (r < 0.60)
        {
            Enter(Phase.FlashOff, t, 0.05 + _rng.NextDouble() * 0.10);
        }
        else if (r < 0.78)
        {
            _param = 0.50 + _rng.NextDouble() * 0.30;
            Enter(Phase.SagDown, t, 0.8 + _rng.NextDouble() * 1.2);
        }
        else if (r < 0.90)
        {
            Enter(Phase.RestrikeOff, t, 0.25 + _rng.NextDouble() * 0.40);
        }
        else if (_mayDie())
        {
            Enter(Phase.Distress, t, 1.5 + _rng.NextDouble() * 2.0);
        }
        else
        {
            Enter(Phase.RestrikeOff, t, 0.25 + _rng.NextDouble() * 0.40);
        }
    }

    private void Enter(Phase phase, double t, double duration)
    {
        _phase = phase;
        _phaseStart = t;
        _phaseDuration = duration;
    }

    private void EnterSteady(double t, bool announce = false)
    {
        _phase = Phase.Steady;
        Intensity = 1;
        Electrode = 0;
        Instability = _bulb.EventRateScale <= 0 ? 0 : 0.06;
        if (announce) _pendingCue = SoundCue.Ignited;
        // exponential inter-event time: sometimes soon, sometimes a quiet stretch
        _nextEventAt = _bulb.EventRateScale <= 0
            ? double.MaxValue
            : t + Math.Max(4.0, -MeanSecondsBetweenEvents / _bulb.EventRateScale * Math.Log(1 - _rng.NextDouble()));
    }

    private double Progress(double t) => _phaseDuration <= 0 ? 1 : (t - _phaseStart) / _phaseDuration;

    private static double Smooth(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }

    private static long Slot(double t, double hz) => (long)(t * hz);

    private static double Hash(long n)
    {
        var x = Math.Sin(n * 12.9898) * 43758.5453;
        return x - Math.Floor(x);
    }
}
