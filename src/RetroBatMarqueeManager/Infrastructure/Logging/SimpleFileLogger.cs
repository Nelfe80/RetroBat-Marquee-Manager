using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace RetroBatMarqueeManager.Infrastructure.Logging
{
    public class SimpleFileLoggerProvider : ILoggerProvider
    {
        private readonly string _path;
        private readonly ConcurrentDictionary<string, SimpleFileLogger> _loggers = new();

        public SimpleFileLoggerProvider(string path)
        {
            _path = path;
            // Create directory if not exists
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        public ILogger CreateLogger(string categoryName)
        {
            return _loggers.GetOrAdd(categoryName, name => new SimpleFileLogger(name, _path));
        }

        public void Dispose()
        {
            _loggers.Clear();
        }
    }

    public class SimpleFileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly string _path;
        private static readonly object _lock = new();

        public SimpleFileLogger(string categoryName, string path)
        {
            _categoryName = categoryName;
            _path = path;
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
                logEntry += Environment.NewLine + exception.ToString();
            }

            lock (_lock)
            {
                try
                {
                    File.AppendAllText(_path, logEntry + Environment.NewLine);
                }
                catch
                {
                    // Ignore write errors to avoid crashing app
                }
            }
        }
    }
}
