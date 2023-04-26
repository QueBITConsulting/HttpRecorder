using System.Net.Http;

namespace QueBIT.HttpRecorder.Factories
{
    /// <summary>
    ///     Factory used to support the creation of items needed to support HAR logging.
    /// </summary>
    public interface IHarLoggingFactory
    {
        /// <summary>
        ///     Returns a delegating handler that will be used to intercept Http messages for us in HAR logging.
        /// </summary>
        /// <param name="handler">
        ///     Optionally specify an existing inner handler to the <see cref="DelegatingHandler" /> instance. A
        ///     new <see cref="HttpClientHandler" /> instance will be created when null.
        /// </param>
        /// <returns>A <see cref="DelegatingHandler" /> instance which can be used in support of HAR logging.</returns>
        DelegatingHandler GetDelegatingHandler(HttpClientHandler handler = null);
    }
}