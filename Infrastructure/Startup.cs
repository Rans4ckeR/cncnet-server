using System.CommandLine;
using Microsoft.Extensions.Configuration;

namespace CnCNetServer;

internal static class Startup
{
    public static IHost BuildApplication(IReadOnlyList<string> args)
    {
        var settings = new HostApplicationBuilderSettings
        {
            Configuration = new ConfigurationManager()
        };
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(settings);

        _ = builder.Services
            .AddOptions<ServiceOptions>().Configure<ParseResult>(ConfigureOptions);
        _ = builder.Services
            .AddSingleton(RootCommandBuilder.Build)
            .AddSingleton<ParseResult>(serviceProvider => serviceProvider.GetRequiredService<RootCommand>().Parse(args))
            .AddWindowsService(static o => o.ServiceName = nameof(CnCNetServer))
            .AddSystemd()
            .AddHostedService<CnCNetBackgroundService>()
            .AddSingleton<TunnelV3>()
#if EnableLegacyVersion
            .AddSingleton<TunnelV2>()
#endif
            .AddTransient<PeerToPeerUtil>()
            .AddHttpClient(Options.DefaultName)
            .ConfigureHttpClient(ConfigureHttpClient)
            .UseSocketsHttpHandler(UseSocketsHttpHandler)
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);

        _ = builder.Logging
#if DEBUG
            .AddConsole()
#endif
            .ConfigureLogging();

        return builder.Build();
    }

    private static void UseSocketsHttpHandler(SocketsHttpHandler socketsHttpHandler, IServiceProvider serviceProvider)
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

    private static void ConfigureHttpClient(IServiceProvider serviceProvider, HttpClient httpClient)
    {
        httpClient.BaseAddress = serviceProvider.GetRequiredService<IOptions<ServiceOptions>>().Value.MasterServerUrl;
        httpClient.Timeout = TimeSpan.FromMilliseconds(10000);
        httpClient.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
    }

    private static void ConfigureOptions(ServiceOptions options, ParseResult parseResult)
    {
        if (parseResult.Errors.Count is not 0)
        {
            Console.Error.WriteLine(FormattableString.Invariant($"Invalid command line arguments:{Environment.NewLine}{string.Join(Environment.NewLine, parseResult.Errors)}"));
            Environment.Exit(CnCNetBackgroundService.ErrorExitCode);
        }

        options.TunnelPort = parseResult.GetRequiredValue(RootCommandBuilder.TunnelPort);
        options.Name = parseResult.GetRequiredValue(RootCommandBuilder.Name);
        options.MaxClients = parseResult.GetRequiredValue(RootCommandBuilder.MaxClients);
        options.NoMasterAnnounce = parseResult.GetRequiredValue(RootCommandBuilder.NoMasterAnnounce);
        options.MasterPassword = parseResult.GetRequiredValue(RootCommandBuilder.MasterPassword);
        options.MaintenancePassword = parseResult.GetRequiredValue(RootCommandBuilder.MaintenancePassword);
        options.MasterServerUrl = parseResult.GetRequiredValue(RootCommandBuilder.MasterServerUrl);
        options.IpLimit = parseResult.GetRequiredValue(RootCommandBuilder.IpLimit);
        options.NoPeerToPeer = parseResult.GetRequiredValue(RootCommandBuilder.NoPeerToPeer);
        options.TunnelV3Enabled = parseResult.GetRequiredValue(RootCommandBuilder.TunnelV3Enabled);
        options.ServerLogLevel = parseResult.GetRequiredValue(RootCommandBuilder.ServerLogLevel);
        options.SystemLogLevel = parseResult.GetRequiredValue(RootCommandBuilder.SystemLogLevel);
        options.AnnounceIpV6 = parseResult.GetRequiredValue(RootCommandBuilder.AnnounceIpV6);
        options.AnnounceIpV4 = parseResult.GetRequiredValue(RootCommandBuilder.AnnounceIpV4);
        options.MaxPacketSize = parseResult.GetRequiredValue(RootCommandBuilder.MaxPacketSize);
        options.MaxPingsGlobal = parseResult.GetRequiredValue(RootCommandBuilder.MaxPingsGlobal);
        options.MaxPingsPerIp = parseResult.GetRequiredValue(RootCommandBuilder.MaxPingsPerIp);
        options.MasterAnnounceInterval = parseResult.GetRequiredValue(RootCommandBuilder.MasterAnnounceInterval);
        options.ClientTimeout = parseResult.GetRequiredValue(RootCommandBuilder.ClientTimeout);
#if EnableLegacyVersion
        options.TunnelV2Enabled = parseResult.GetRequiredValue(RootCommandBuilder.TunnelV2Enabled);
        options.TunnelV2Port = parseResult.GetRequiredValue(RootCommandBuilder.TunnelV2Port);
        options.TunnelV2Https = parseResult.GetRequiredValue(RootCommandBuilder.TunnelV2Https);
#endif
    }
}