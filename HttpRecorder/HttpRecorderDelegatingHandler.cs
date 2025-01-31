﻿using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using QueBIT.HttpRecorder.Anonymizers;
using QueBIT.HttpRecorder.Matchers;
using QueBIT.HttpRecorder.Repositories;
using QueBIT.HttpRecorder.Repositories.HAR;

namespace QueBIT.HttpRecorder
{
    /// <summary>
    /// <see cref="DelegatingHandler" /> that records HTTP interactions for integration tests.
    /// </summary>
    public class HttpRecorderDelegatingHandler : DelegatingHandler
    {
        /// <summary>
        /// Gets the name of the environment variable that allows overriding of the <see cref="Mode"/>.
        /// </summary>
        public const string OverridingEnvironmentVariableName = "HTTP_RECORDER_MODE";

        private readonly IRequestMatcher _matcher;
        private readonly IInteractionRepository _repository;
        private readonly IInteractionAnonymizer _anonymizer;
        private bool _disposed = false;
        private HttpRecorderMode? _executionMode;

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpRecorderDelegatingHandler" /> class.
        /// </summary>
        /// <param name="interactionName">
        /// The name of the interaction.
        /// If you use the default <see cref="IInteractionRepository"/>, this will be the path to the HAR file (relative or absolute) and
        /// if no file extension is provided, .har will be used.
        /// </param>
        /// <param name="mode">The <see cref="HttpRecorderMode" />. Defaults to <see cref="HttpRecorderMode.Auto" />.</param>
        /// <param name="matcher">
        /// The <see cref="IRequestMatcher"/> to use to match interactions with incoming <see cref="HttpRequestMessage"/>.
        /// Defaults to matching Once by <see cref="HttpMethod"/> and <see cref="HttpRequestMessage.RequestUri"/>.
        /// <see cref="RulesMatcher.ByHttpMethod"/> and <see cref="RulesMatcher.ByRequestUri"/>.
        /// </param>
        /// <param name="repository">
        /// The <see cref="IInteractionRepository"/> to use to read/write the interaction.
        /// Defaults to <see cref="HttpArchiveInteractionRepository"/>.
        /// </param>
        /// <param name="anonymizer">
        /// The <see cref="IInteractionAnonymizer"/> to use to anonymize the interaction.
        /// Defaults to <see cref="RulesInteractionAnonymizer.Default"/>.
        /// </param>
        public HttpRecorderDelegatingHandler(
            string interactionName,
            HttpRecorderMode mode = HttpRecorderMode.Auto,
            IRequestMatcher matcher = null,
            IInteractionRepository repository = null,
            IInteractionAnonymizer anonymizer = null)
        {
            InteractionName = interactionName;
            Mode = mode;
            _matcher = matcher ?? RulesMatcher.MatchOnce.ByHttpMethod().ByRequestUri();
            _repository = repository ?? new HttpArchiveInteractionRepository();
            _anonymizer = anonymizer ?? RulesInteractionAnonymizer.Default;
        }

        /// <summary>
        /// Gets the name of the interaction.
        /// </summary>
        public string InteractionName { get; }

        /// <summary>
        /// Gets the <see cref="HttpRecorderMode" />.
        /// </summary>
        public HttpRecorderMode Mode { get; }

        /// <summary>
        /// Creates an instance of an HTTP Handler using a name and an Ilogger for logging.
        /// </summary>
        /// <param name="name">The name of this HTTP Client.</param>
        /// <param name="logger">The Ilogger to use for the repository.</param>
        /// <param name="innerHandler">an optional inner handler.</param>
        /// <param name="installHandlerEvenIfLoggingIsDisabled">By default HAR logger handler is not installed if logging is not enabled. If logging will become enabled later then set this to false to install a HAR Logger handler that can be turned on or off</param>
        /// <returns>an HttpRecorderDelegatingHandler.</returns>
        public static HttpMessageHandler CreateInstance(string name, ILogger logger, HttpMessageHandler innerHandler = null, bool installHandlerEvenIfLoggingIsDisabled = false)
        {
            // If we only want a HAR Logger if logging is enabled right NOW then we should not install one if logging is disabled now.
            if (!installHandlerEvenIfLoggingIsDisabled)
            {
                // If logging is disabled then NO HAR LOGGER!
                if (!logger.IsEnabled(LogLevel.Trace))
                {
                    return innerHandler ?? new HttpClientHandler();
                }
            }

            // Install a HAR LOGGER HANDLER which can be turned on or off based on logger.IsEnabled
            return new HttpRecorderDelegatingHandler(name, HttpRecorderMode.Record, null, new LoggerInteractionRepository(logger))
            {
                InnerHandler = innerHandler ?? new HttpClientHandler(),
            };
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            base.Dispose(disposing);
        }

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (Mode == HttpRecorderMode.Passthrough)
            {
                var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                return response;
            }

            await ResolveExecutionMode(cancellationToken);

            //if (_executionMode == HttpRecorderMode.Replay)
            //{
            //    if (_interaction == null)
            //    {
            //        _interaction = await _repository.LoadAsync(InteractionName, cancellationToken);
            //    }

            //    var interactionMessage = _matcher.Match(request, _interaction);
            //    if (interactionMessage == null)
            //    {
            //        throw new HttpRecorderException($"Unable to find a matching interaction for request {request.Method} {request.RequestUri}.");
            //    }

            //    return await PostProcessResponse(interactionMessage.Response);
            //}

            var start = DateTimeOffset.Now;
            var sw = Stopwatch.StartNew();
            var innerResponse = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            sw.Stop();

            var newInteractionMessage = new InteractionMessage(innerResponse, new InteractionMessageTimings(start, sw.Elapsed));
            var interaction = new Interaction(InteractionName, new[] { newInteractionMessage });

            interaction = await _anonymizer.Anonymize(interaction, cancellationToken);
            interaction = await _repository.StoreAsync(interaction, cancellationToken);

            return await PostProcessResponse(newInteractionMessage.Response);

        }

        /// <summary>
        /// Resolves the current <see cref="_executionMode"/>.
        /// Handles <see cref="OverridingEnvironmentVariableName"/> and <see cref="HttpRecorderMode.Auto"/>, if they are set (in that priority order),
        /// otherwise uses the current <see cref="Mode"/>.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token to cancel operation.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        private async Task ResolveExecutionMode(CancellationToken cancellationToken)
        {
            if (!_executionMode.HasValue)
            {
                var overridingEnvVarValue = Environment.GetEnvironmentVariable(OverridingEnvironmentVariableName);
                if (!string.IsNullOrWhiteSpace(overridingEnvVarValue) && Enum.TryParse<HttpRecorderMode>(overridingEnvVarValue, out var parsedOverridingEnvVarValue))
                {
                    _executionMode = parsedOverridingEnvVarValue;
                    return;
                }

                if (Mode == HttpRecorderMode.Auto)
                {
                    _executionMode = (await _repository.ExistsAsync(InteractionName, cancellationToken))
                        ? HttpRecorderMode.Replay
                        : HttpRecorderMode.Record;

                    return;
                }

                _executionMode = Mode;
            }
        }

        /// <summary>
        /// Custom processing on <see cref="HttpResponseMessage"/> to better simulate a real response from the network
        /// and allow replayability.
        /// </summary>
        /// <param name="response">The <see cref="HttpResponseMessage"/>.</param>
        /// <returns>The <see cref="HttpResponseMessage"/> returned as convenience.</returns>
        private async Task<HttpResponseMessage> PostProcessResponse(HttpResponseMessage response)
        {
            if (response.Content != null)
            {
                var stream = await response.Content.ReadAsStreamAsync();
                if (stream.CanSeek)
                {
                    // The HTTP Client is adding the content length header on HttpConnectionResponseContent even when the server does not have a header.
                    response.Content.Headers.ContentLength = stream.Length;

                    // We do reset the stream in case it needs to be re-read.
                    stream.Seek(0, SeekOrigin.Begin);
                }
            }

            return response;
        }
    }
}
