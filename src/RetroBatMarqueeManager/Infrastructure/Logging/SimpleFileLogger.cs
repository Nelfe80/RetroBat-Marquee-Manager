using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;

namespace RetroBatMarqueeManager.Infrastructure.Logging
{
    /// <summary>
    /// Asynchronous file logger (docs\Update.txt §3/§9). The former implementation
    /// did File.AppendAllText under a global lock for EVERY message — an open/write/
    /// close syscall per line, serialized across all threads, on the hot navigation
    /// path. This version hands formatted lines to a bounded channel (never blocks the
    /// caller) drained by a single background writer that keeps one StreamWriter open,
    /// flushes in batches, and rotates the file (10 MiB × 3). No disk wait ever sits on
    /// the MEM/output/navigation path.
    /// </summary>
    public sealed class SimpleFileLoggerProvider : ILoggerProvider
    {
        private const long RotateBytes = 10L * 1024 * 1024;
        private const int KeepFiles = 3;

        private readonly string _path;
        private readonly ConcurrentDictionary<string, SimpleFileLogger> _loggers = new();
        private readonly Channel<string> _channel = Channel.CreateBounded<string>(
            new BoundedChannelOptions(16384)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropWrite // a log flood never stalls the app
            });
        private readonly Task _writer;

        public SimpleFileLoggerProvider(string path)
        {
            _path = path;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            _writer = Task.Run(WriteLoopAsync);
        }

        public ILogger CreateLogger(string categoryName)
            => _loggers.GetOrAdd(categoryName, name => new SimpleFileLogger(name, this));

        /// <summary>Non-blocking enqueue; a full channel drops the line rather than
        /// stall the caller (the app never waits on the disk).</summary>
        internal void Enqueue(string line) => _channel.Writer.TryWrite(line);

        private async Task WriteLoopAsync()
        {
            StreamWriter? stream = null;
            long written = 0;
            try
            {
                (stream, written) = OpenStream();
                var reader = _channel.Reader;
                while (await reader.WaitToReadAsync().ConfigureAwait(false))
                {
                    while (reader.TryRead(out var line))
                    {
                        if (written >= RotateBytes)
                        {
                            stream.Dispose();
                            Rotate();
                            (stream, written) = OpenStream();
                        }
                        stream.WriteLine(line);
                        written += line.Length + Environment.NewLine.Length;
                    }
                    // batch flush: one flush per drained burst, not per line
                    await stream.FlushAsync().ConfigureAwait(false);
                }
            }
            catch
            {
                // logging must never crash the app
            }
            finally
            {
                stream?.Dispose();
            }
        }

        private (StreamWriter Stream, long Bytes) OpenStream()
        {
            var info = new FileInfo(_path);
            var bytes = info.Exists ? info.Length : 0;
            var stream = new StreamWriter(new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read), Encoding.UTF8);
            return (stream, bytes);
        }

        /// <summary>debug.log → debug.log.1 → debug.log.2, oldest dropped.</summary>
        private void Rotate()
        {
            try
            {
                var oldest = $"{_path}.{KeepFiles - 1}";
                if (File.Exists(oldest)) File.Delete(oldest);
                for (var i = KeepFiles - 2; i >= 1; i--)
                {
                    var from = $"{_path}.{i}";
                    if (File.Exists(from)) File.Move(from, $"{_path}.{i + 1}", overwrite: true);
                }
                if (File.Exists(_path)) File.Move(_path, $"{_path}.1", overwrite: true);
            }
            catch
            {
                // rotation failure just means we keep appending to the current file
            }
        }

        public void Dispose()
        {
            _channel.Writer.TryComplete();
            try { _writer.Wait(TimeSpan.FromSeconds(3)); } catch { /* drain best effort */ }
            _loggers.Clear();
        }
    }

    public sealed class SimpleFileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly SimpleFileLoggerProvider _provider;

        public SimpleFileLogger(string categoryName, SimpleFileLoggerProvider provider)
        {
            _categoryName = categoryName;
            _provider = provider;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception);
            var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{logLevel}] [{_categoryName}] {message}";
            if (exception != null)
            {
                logEntry += Environment.NewLine + exception;
            }
            _provider.Enqueue(logEntry);
        }
    }
}
