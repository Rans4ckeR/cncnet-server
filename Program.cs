using System.CommandLine;
using System.CommandLine.Hosting;
using CnCNetServer;

return await new CommandLineConfiguration(RootCommandBuilder.Build())
    .UseHost(Host.CreateDefaultBuilder, static hostBuilder =>
        hostBuilder
            .ConfigureServices(static services =>
            {
                services
                    .AddOptions<ServiceOptions>()
                    .BindCommandLine();
                services
                    .AddWindowsService(static o => o.ServiceName = "CnCNetServer")
                    .AddSystemd()
                    .AddHostedService<CnCNetBackgroundService>()
                    .AddSingleton<TunnelV3>()
#if EnableLegacyVersion
                    .AddSingleton<TunnelV2>()
#endif
                    .AddTransient<PeerToPeerUtil>()
                    .AddHttpClient(Options.DefaultName)
                    .ConfigureHttpClient(Startup.ConfigureHttpClient)
                    .UseSocketsHttpHandler(Startup.UseSocketsHttpHandler)
                    .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
            })
            .ConfigureLogging(Startup.ConfigureLogging))
    .InvokeAsync(args)
    .ConfigureAwait(ConfigureAwaitOptions.None);