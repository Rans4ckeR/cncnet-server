using System.CommandLine;
using System.CommandLine.Hosting;
using System.CommandLine.Parsing;

namespace CnCNetServer;

internal static class RootCommandBuilder
{
    public static RootCommand Build()
    {
        var nameOption = new Option<string>("--name", "-n") { Description = "Name of the server", Required = true };
        var maxClientsOption = new Option<int>("--max-clients", "-m") { Description = "Maximum clients allowed on the tunnel server", DefaultValueFactory = static _ => 200 };
        var addressLimitOption = new Option<int>("--ip-limit", "-i") { Description = "Maximum clients allowed per IP address", DefaultValueFactory = static _ => 8 };
        var tunnelPortOption = new Option<int>("--tunnel-port", "-p") { Description = "Port used for the V3 tunnel server", DefaultValueFactory = static _ => 50001 };
#if EnableLegacyVersion
        var tunnelV2PortOption = new Option<int>("--tunnel-v2-port", "-p2") { Description = "Port used for the V2 tunnel server", DefaultValueFactory = static _ => 50000 };
#endif
        var announceIpV6Option = new Option<bool>("--announce-ipv6", "-6") { Description = "Announce IPv6 address to master server", DefaultValueFactory = static _ => true };
        var announceIpV4Option = new Option<bool>("--announce-ipv4", "-4") { Description = "Announce IPv4 address to master server", DefaultValueFactory = static _ => true };
        var maxPacketSizeOption = new Option<int>("--max-packet-size", "-mps") { Description = "Maximum accepted packet size", DefaultValueFactory = static _ => 2048 };
        var maxPingsGlobalOption = new Option<ushort>("--max-pings-global", "-mpg") { Description = "Maximum accepted ping requests globally", DefaultValueFactory = static _ => 1024 };
        var maxPingsPerIpOption = new Option<ushort>("--max-pings-per-ip", "-mpi") { Description = "Maximum accepted ping requests per IP", DefaultValueFactory = static _ => 20 };
        var masterAnnounceIntervalOption = new Option<ushort>("-master-announce-interval", "-ai") { Description = "Master server announce interval in seconds", DefaultValueFactory = static _ => 60 };
        var clientTimeoutOption = new Option<int>("--client-timeout", "-c") { Description = "Client timeout in seconds", DefaultValueFactory = static _ => 60 };

        nameOption.Validators.Add(static result =>
        {
            if (result.GetValueOrDefault<string>().Any(static q => q is ';'))
                result.AddError(FormattableString.Invariant($"{nameof(ServiceOptions.Name)} cannot contain the character ;"));
        });
        maxClientsOption.Validators.Add(static result =>
        {
            const int minMaxClients = 2;

            if (result.GetValueOrDefault<int>() < minMaxClients)
                result.AddError(FormattableString.Invariant($"{nameof(ServiceOptions.MaxClients)} minimum is {minMaxClients}"));
        });
        addressLimitOption.Validators.Add(static result =>
        {
            const int minIpLimit = 1;

            if (result.GetValueOrDefault<int>() < minIpLimit)
                result.AddError(FormattableString.Invariant($"{nameof(ServiceOptions.IpLimit)} minimum is {minIpLimit}"));
        });
        maxPacketSizeOption.Validators.Add(static result =>
        {
            const int maxPacketSizeLimit = 512;

            if (result.GetValueOrDefault<int>() < maxPacketSizeLimit)
                result.AddError(FormattableString.Invariant($"{nameof(ServiceOptions.MaxPacketSize)} minimum is {maxPacketSizeLimit}"));
        });
        clientTimeoutOption.Validators.Add(static result =>
        {
            const int minClientTimeout = 30;

            if (result.GetValueOrDefault<int>() < minClientTimeout)
                result.AddError(FormattableString.Invariant($"{nameof(ServiceOptions.ClientTimeout)} minimum is {minClientTimeout}"));
        });
        tunnelPortOption.Validators.Add(ValidatePort);
#if EnableLegacyVersion
        tunnelV2PortOption.Validators.Add(ValidatePort);
#endif
        announceIpV6Option.Validators.Add(static result => ValidateIpAnnounce(result, Socket.OSSupportsIPv6));
        announceIpV4Option.Validators.Add(static result => ValidateIpAnnounce(result, Socket.OSSupportsIPv4));

        var rootCommand = new RootCommand("CnCNet tunnel server")
        {
            nameOption,
            tunnelPortOption,
#if EnableLegacyVersion
            tunnelV2PortOption,
#endif
            maxClientsOption,
            new Option<bool>("--no-master-announce", "-nm") { Description = "Don't register to master", DefaultValueFactory = static _ => false },
            new Option<string?>("--master-password", "-masp") { Description = "Master password", DefaultValueFactory = static _ => null },
            new Option<string?>("--maintenance-password", "-maip") { Description = "Maintenance password", DefaultValueFactory = static _ => null },
            new Option<Uri>("--master-server-url", "-mu") { Description = "Master server URL", DefaultValueFactory = static _ => new(FormattableString.Invariant($"{Uri.UriSchemeHttps}{Uri.SchemeDelimiter}cncnet.org/api/v1/master-announce")) },
            addressLimitOption,
            new Option<bool>("--no-peer-to-peer", "-np") { Description = "Disable STUN NAT traversal server (UDP 8054 & 3478)", DefaultValueFactory = static _ => false },
            new Option<bool>("--tunnel-v3-enabled", "-3") { Description = "Start a V3 tunnel server", DefaultValueFactory = static _ => true },
#if EnableLegacyVersion
            new Option<bool>("--tunnel-v2-enabled", "-2") { Description = "Start a V2 tunnel server", DefaultValueFactory = static _ => true },
#endif
            new Option<LogLevel>("--server-log-level", "-sel") { Description = "CnCNet server messages log level", DefaultValueFactory = static _ => LogLevel.Warning },
            new Option<LogLevel>("--system-log-level", "-syl") { Description = "Low level system messages log level", DefaultValueFactory = static _ => LogLevel.Warning },
            announceIpV6Option,
            announceIpV4Option,
#if EnableLegacyVersion
            new Option<bool>("--tunnel-v2-https", "-h") { Description = FormattableString.Invariant($"Use {Uri.UriSchemeHttps} Tunnel V2 web server"), DefaultValueFactory = static _ => false },
#endif
            maxPacketSizeOption,
            maxPingsGlobalOption,
            maxPingsPerIpOption,
            masterAnnounceIntervalOption,
            clientTimeoutOption
        };

        rootCommand.SetAction(static (parseResult, cancellationToken) => parseResult.GetHost().WaitForShutdownAsync(cancellationToken));

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