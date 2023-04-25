using System.Net.Http;

namespace QueBIT.HttpRecorder
{
    public class NullDelegatingHandler : DelegatingHandler
    {
        public NullDelegatingHandler()
        {
            InnerHandler = new HttpClientHandler();
        }
    }
}