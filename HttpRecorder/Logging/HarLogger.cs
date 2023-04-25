using QueBIT.HttpRecorder.Repositories.HAR;
using System;
using Microsoft.Extensions.Logging;

namespace QueBIT.HttpRecorder.Logging
{
    public class HarLogger : IHarLogger, ILogger
    {
        public void LogHarArchive(string loggerName, HttpArchive archive)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state)
        {
            throw new NotImplementedException();
        }
    }
}
