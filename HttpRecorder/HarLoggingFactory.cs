using System.Net.Http;
using Microsoft.Extensions.Logging;
using QueBIT.HttpRecorder.Logging;
using QueBIT.HttpRecorder.Repositories;
using QueBIT.HttpRecorder.Repositories.HAR;

namespace QueBIT.HttpRecorder
{
    public static class HarLoggingFactory
    {
        public static DelegatingHandler CreateHarLoggingHandlerFromLogger(string harLoggerName, ILogger logger, HttpClientHandler handler = null, IInteractionRepository repository = null)
        {
            if (logger is IHarLogger harLogger)
            {
                if (!logger.IsEnabled(LogLevel.Trace))
                    return new NullDelegatingHandler();
                
                return new HttpRecorderDelegatingHandler(harLoggerName, HttpRecorderMode.Record, null, repository ?? new LoggerInteractionRepository(logger))
                {
                    InnerHandler = handler ?? new HttpClientHandler()
                };
            }

            return new NullDelegatingHandler();
        }
    }
}