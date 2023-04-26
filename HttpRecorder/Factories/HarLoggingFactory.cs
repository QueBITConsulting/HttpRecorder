using System.Net.Http;
using Microsoft.Extensions.Logging;
using QueBIT.HttpRecorder.Logging;
using QueBIT.HttpRecorder.Repositories;
using QueBIT.HttpRecorder.Repositories.HAR;

namespace QueBIT.HttpRecorder.Factories
{
    /// <summary>
    ///     General implementation of a factory used to create items needed to support HAR logging.
    /// </summary>
    public class HarLoggingFactory : IHarLoggingFactory
    {
        private readonly string _harLoggerName;
        private readonly ILogger _logger;
        private readonly IInteractionRepository _repository;

        /// <summary>
        ///     Initializes a new instance of the <see cref="HarLoggingFactory" /> class.
        /// </summary>
        /// <param name="harLoggerName">The name to give to the HAR logger.</param>
        /// <param name="logger">The <see cref="ILogger" /> instance needed to support some HAR logging operations.</param>
        /// <param name="repository">
        ///     The optional <see cref="IInteractionRepository" /> instance needed to record interactions for
        ///     HAR logging.
        /// </param>
        public HarLoggingFactory(string harLoggerName, ILogger logger, IInteractionRepository repository = null)
        {
            _harLoggerName = harLoggerName;
            _logger = logger;
            _repository = repository;
        }

        /// <inheritdoc />
        public DelegatingHandler GetDelegatingHandler(HttpClientHandler handler = null)
        {
            if (IsHarLoggingEnabled())
            {
                return new HttpRecorderDelegatingHandler(_harLoggerName, HttpRecorderMode.Record, null, _repository ?? new LoggerInteractionRepository(_logger))
                {
                    InnerHandler = handler ?? new HttpClientHandler()
                };
            }

            return new NullDelegatingHandler();
        }

        /// <summary>
        ///     Helper method to determine if HAR logging is enabled.
        /// </summary>
        private bool IsHarLoggingEnabled()
        {
            if (_logger is IHarLogger)
            {
                return _logger.IsEnabled(LogLevel.Trace);
            }

            return false;
        }
    }
}