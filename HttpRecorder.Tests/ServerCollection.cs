using Xunit;

namespace QueBIT.HttpRecorder.Tests
{
    [CollectionDefinition(ServerCollection.Name)]
    public class ServerCollection : ICollectionFixture<ServerFixture>
    {
        public const string Name = "Server";
    }
}
