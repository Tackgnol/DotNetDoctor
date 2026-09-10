using System.Net.Http;

namespace DisabledDefault;

public sealed class Leaky
{
    public int CountHandlers()
    {
        var handler = new HttpClientHandler();
        handler.MaxConnectionsPerServer = 4;
        return handler.MaxConnectionsPerServer;
    }
}
