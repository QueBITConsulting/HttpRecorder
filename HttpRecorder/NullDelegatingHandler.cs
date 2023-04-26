using System.Net.Http;

namespace QueBIT.HttpRecorder
{
    /// <summary>
    ///     Returns a generic delegating handler to use when HAR logging is not enabled.
    /// </summary>
    public class NullDelegatingHandler : DelegatingHandler
    {
        /// <summary>
        ///     Creates a new instance of the <see cref="NullDelegatingHandler" /> class with a new
        ///     <see cref="HttpClientHandler" /> instance.
        /// </summary>
        public NullDelegatingHandler()
        {
            InnerHandler = new HttpClientHandler();
        }
    }
}