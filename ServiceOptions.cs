#pragma warning disable CA1812 // Avoid uninstantiated internal classes
namespace CnCNetServer;

internal sealed record ServiceOptions
{
    public required int TunnelPort { get; set; }

    public required string Name { get; set; }

    public required int MaxClients { get; set; }

    public required bool NoMasterAnnounce { get; set; }

    public required string? MasterPassword { get; set; }

    public required string? MaintenancePassword { get; set; }

    public required Uri MasterServerUrl { get; set; }

    public required int IpLimit { get; set; }

    public required bool NoPeerToPeer { get; set; }

    public required bool TunnelV3Enabled { get; set; }

    public required LogLevel ServerLogLevel { get; set; }

    public required LogLevel SystemLogLevel { get; set; }

    public required bool AnnounceIpV6 { get; set; }

    public required bool AnnounceIpV4 { get; set; }

    public required int MaxPacketSize { get; set; }

    public required ushort MaxPingsGlobal { get; set; }

    public required ushort MaxPingsPerIp { get; set; }

    public required ushort MasterAnnounceInterval { get; set; }

    public required int ClientTimeout { get; set; }
#if EnableLegacyVersion

    public required bool TunnelV2Enabled { get; set; }

    public required int TunnelV2Port { get; set; }

    public required bool TunnelV2Https { get; set; }
#endif
}