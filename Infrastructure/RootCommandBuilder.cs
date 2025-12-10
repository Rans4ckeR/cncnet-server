using System.CommandLine;
using System.CommandLine.Parsing;

namespace CnCNetServer;

internal static class RootCommandBuilder
{
    public static Option<int> TunnelPort { get; } = new("--tunnel-port", "-p") { Description = "Port used for the V3 tunnel server", DefaultValueFactory = static _ => 50001 };

    public static Option<string> Name { get; } = new("--name", "-n") { Description = "Name of the server", Required = true };

    public static Option<int> MaxClients { get; } = new("--max-clients", "-m") { Description = "Maximum clients allowed on the tunnel server", DefaultValueFactory = static _ => 200 };

    public static Option<bool> NoMasterAnnounce { get; } = new("--no-master-announce", "-nm") { Description = "Don't register to master", DefaultValueFactory = static _ => false };

    public static Option<string?> MasterPassword { get; } = new("--master-password", "-masp") { Description = "Master password", DefaultValueFactory = static _ => null };

    public static Option<string?> MaintenancePassword { get; } = new("--maintenance-password", "-maip") { Description = "Maintenance password", DefaultValueFactory = static _ => null };

    public static Option<Uri> MasterServerUrl { get; } = new("--master-server-url", "-mu") { Description = "Master server URL", DefaultValueFactory = static _ => new(FormattableString.Invariant($"{Uri.UriSchemeHttps}{Uri.SchemeDelimiter}cncnet.org/api/v1/master-announce")) };

    public static Option<int> IpLimit { get; } = new("--ip-limit", "-i") { Description = "Maximum clients allowed per IP address", DefaultValueFactory = static _ => 8 };

    public static Option<bool> NoPeerToPeer { get; } = new("--no-peer-to-peer", "-np") { Description = "Disable STUN NAT traversal server (UDP 8054 & 3478)", DefaultValueFactory = static _ => false };

    public static Option<bool> TunnelV3Enabled { get; } = new("--tunnel-v3-enabled", "-3") { Description = "Start a V3 tunnel server", DefaultValueFactory = static _ => true };

    public static Option<LogLevel> ServerLogLevel { get; } = new("--server-log-level", "-sel") { Description = "CnCNet server messages log level", DefaultValueFactory = static _ => LogLevel.Warning };

    public static Option<LogLevel> SystemLogLevel { get; } = new("--system-log-level", "-syl") { Description = "Low level system messages log level", DefaultValueFactory = static _ => LogLevel.Warning };

    public static Option<bool> AnnounceIpV6 { get; } = new("--announce-ipv6", "-6") { Description = "Announce IPv6 address to master server", DefaultValueFactory = static _ => true };

    public static Option<bool> AnnounceIpV4 { get; } = new("--announce-ipv4", "-4") { Description = "Announce IPv4 address to master server", DefaultValueFactory = static _ => true };

    public static Option<int> MaxPacketSize { get; } = new("--max-packet-size", "-mps") { Description = "Maximum accepted packet size", DefaultValueFactory = static _ => 2048 };

    public static Option<ushort> MaxPingsGlobal { get; } = new("--max-pings-global", "-mpg") { Description = "Maximum accepted ping requests globally", DefaultValueFactory = static _ => 1024 };

    public static Option<ushort> MaxPingsPerIp { get; } = new("--max-pings-per-ip", "-mpi") { Description = "Maximum accepted ping requests per IP", DefaultValueFactory = static _ => 20 };

    public static Option<ushort> MasterAnnounceInterval { get; } = new("-master-announce-interval", "-ai") { Description = "Master server announce interval in seconds", DefaultValueFactory = static _ => 60 };

    public static Option<int> ClientTimeout { get; } = new("--client-timeout", "-c") { Description = "Client timeout in seconds", DefaultValueFactory = static _ => 60 };

#if EnableLegacyVersion
    public static Option<bool> TunnelV2Enabled { get; } = new("--tunnel-v2-enabled", "-2") { Description = "Start a V2 tunnel server", DefaultValueFactory = static _ => true };

    public static Option<int> TunnelV2Port { get; } = new("--tunnel-v2-port", "-p2") { Description = "Port used for the V2 tunnel server", DefaultValueFactory = static _ => 50000 };

    public static Option<bool> TunnelV2Https { get; } = new("--tunnel-v2-https", "-h") { Description = FormattableString.Invariant($"Use {Uri.UriSchemeHttps} Tunnel V2 web server"), DefaultValueFactory = static _ => false };

#endif
    public static RootCommand Build(IServiceProvider serviceProvider)
    {
        Name.Validators.Add(static result =>
        {
            if (result.GetValueOrDefault<string>().Any(static q => q is ';'))
                result.AddError(FormattableString.Invariant($"{Name} cannot contain the character ;"));
        });
        MaxClients.Validators.Add(static result =>
        {
            const int minMaxClients = 2;

            if (result.GetValueOrDefault<int>() < minMaxClients)
                result.AddError(FormattableString.Invariant($"{nameof(MaxClients)} minimum is {minMaxClients}"));
        });
        IpLimit.Validators.Add(static result =>
        {
            const int minIpLimit = 1;

            if (result.GetValueOrDefault<int>() < minIpLimit)
                result.AddError(FormattableString.Invariant($"{nameof(IpLimit)} minimum is {minIpLimit}"));
        });
        MaxPacketSize.Validators.Add(static result =>
        {
            const int maxPacketSizeLimit = 512;

            if (result.GetValueOrDefault<int>() < maxPacketSizeLimit)
                result.AddError(FormattableString.Invariant($"{nameof(MaxPacketSize)} minimum is {maxPacketSizeLimit}"));
        });
        ClientTimeout.Validators.Add(static result =>
        {
            const int minClientTimeout = 30;

            if (result.GetValueOrDefault<int>() < minClientTimeout)
                result.AddError(FormattableString.Invariant($"{nameof(ClientTimeout)} minimum is {minClientTimeout}"));
        });
        TunnelV3Enabled.Validators.Add(static tunnelV3EnabledResult =>
        {
            NoPeerToPeer.Validators.Add(noPeerToPeerResult =>
#pragma warning disable format
            {
#if EnableLegacyVersion
                TunnelV2Enabled.Validators.Add(tunnelV2EnabledResult =>
                {
                    bool tunnelEnabled = tunnelV3EnabledResult.GetValueOrDefault<bool>() || tunnelV2EnabledResult.GetValueOrDefault<bool>();
#else
                    bool tunnelEnabled = tunnelV3EnabledResult.GetValueOrDefault<bool>();
#endif

                    if (!tunnelEnabled && noPeerToPeerResult.GetValueOrDefault<bool>())
                        noPeerToPeerResult.AddError("No tunnel or peer to peer enabled.");
                });
#pragma warning restore format
#if EnableLegacyVersion
            });
#endif
        });
        TunnelPort.Validators.Add(ValidatePort);
#if EnableLegacyVersion
        TunnelV2Port.Validators.Add(ValidatePort);
#endif
        AnnounceIpV6.Validators.Add(static result => ValidateIpAnnounce(result, Socket.OSSupportsIPv6));
        AnnounceIpV4.Validators.Add(static result => ValidateIpAnnounce(result, Socket.OSSupportsIPv4));

        var rootCommand = new RootCommand("CnCNet tunnel server")
        {
            TunnelPort,
            Name,
            MaxClients,
            NoMasterAnnounce,
            MasterPassword,
            MaintenancePassword,
            MasterServerUrl,
            IpLimit,
            NoPeerToPeer,
            TunnelV3Enabled,
            ServerLogLevel,
            SystemLogLevel,
            AnnounceIpV6,
            AnnounceIpV4,
            MaxPacketSize,
            MaxPingsGlobal,
            MaxPingsPerIp,
            MasterAnnounceInterval,
            ClientTimeout,
#if EnableLegacyVersion
            TunnelV2Enabled,
            TunnelV2Port,
            TunnelV2Https
#endif
        };

        rootCommand.SetAction((_, cancellationToken) => serviceProvider.GetRequiredService<IHost>().WaitForShutdownAsync(cancellationToken));

        return rootCommand;
    }

    private static void ValidatePort(OptionResult result)
    {
        const int minPort = 1024;
        const int maxPort = 65534;

        if (result.GetValueOrDefault<int>() is < minPort or > maxPort)
            result.AddError(FormattableString.Invariant($"{result.Option.Name} minimum is {minPort} and maximum is {maxPort}"));
    }

    private static void ValidateIpAnnounce(OptionResult result, bool isSupported)
    {
        if (result.GetValueOrDefault<bool>() && !isSupported)
            result.AddError(FormattableString.Invariant($"{result.Option.Name} is not supported on this system"));
    }
}