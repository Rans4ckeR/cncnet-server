using System.CommandLine;
using System.CommandLine.Hosting;

namespace CnCNetServer;

internal static class Startup
{
    public static void UseSocketsHttpHandler(SocketsHttpHandler socketsHttpHandler, IServiceProvider serviceProvider)
    {
        socketsHttpHandler.AutomaticDecompression = DecompressionMethods.All;
        socketsHttpHandler.PooledConnectionLifetime = TimeSpan.FromMinutes(15);
        socketsHttpHandler.ConnectCallback = async (context, token) =>
        {
            Socket? socket = null;

            try
            {
                socket = serviceProvider.GetRequiredService<IOptions<ServiceOptions>>().Value.AnnounceIpV4
                    ? new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
                    : new(SocketType.Stream, ProtocolType.Tcp);

                socket.NoDelay = true;

                await socket.ConnectAsync(context.DnsEndPoint, token).ConfigureAwait(false);

                return new NetworkStream(socket, true);
            }
            catch
            {
                socket?.Dispose();

                throw;
            }
        };
    }

    public static void ConfigureHttpClient(IServiceProvider serviceProvider, HttpClient httpClient)
    {
        httpClient.BaseAddress = serviceProvider.GetRequiredService<IOptions<ServiceOptions>>().Value.MasterServerUrl;
        httpClient.Timeout = TimeSpan.FromMilliseconds(10000);
        httpClient.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
    }

    public static void ConfigureLogging(HostBuilderContext context, ILoggingBuilder builder)
    {
        ParseResult parseResult = context.GetParseResult();
        LogLevel serverLogLevel = parseResult.GetRequiredValue<LogLevel>("--server-log-level");
        LogLevel systemLogLevel = parseResult.GetRequiredValue<LogLevel>("--system-log-level");

        builder.ConfigureLogging(serverLogLevel, systemLogLevel);
    }
}