using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text;

namespace ShowCuePlayer.Services;

/// <summary>Small, dependency-free rolling daily file logger.</summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private readonly object _writeGate = new();

    public FileLoggerProvider(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(name, Write));

    private void Write(string line)
    {
        try
        {
            lock (_writeGate)
            {
                Directory.CreateDirectory(_directory);
                File.AppendAllText(Path.Combine(_directory, $"showcueplayer-{DateTime.UtcNow:yyyyMMdd}.log"), line, Encoding.UTF8);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose() => _loggers.Clear();

    private sealed class FileLogger : ILogger
    {
        private readonly string _category;
        private readonly Action<string> _writer;

        public FileLogger(string category, Action<string> writer)
        {
            _category = category;
            _writer = writer;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var message = formatter(state, exception);
            _writer($"{DateTimeOffset.Now:O} [{logLevel}] {_category}: {message}{Environment.NewLine}{exception}{Environment.NewLine}");
        }
    }
}
