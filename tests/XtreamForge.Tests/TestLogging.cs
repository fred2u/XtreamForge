using Microsoft.Extensions.Logging;

namespace XtreamForge.Tests;

internal sealed class TestLogSink
{
    private readonly List<string> _messages = [];

    public IReadOnlyList<string> Messages => _messages;

    public void Add(string message)
    {
        lock (_messages)
        {
            _messages.Add(message);
        }
    }
}

internal sealed class TestLoggerProvider(TestLogSink sink) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new TestLogger(categoryName, sink);

    public void Dispose()
    {
    }

    private sealed class TestLogger(string categoryName, TestLogSink sink) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = $"{categoryName}|{logLevel}|{formatter(state, exception)}";
            if (exception is not null)
            {
                message = $"{message}|{exception.GetType().Name}|{exception.Message}";
            }

            sink.Add(message);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
