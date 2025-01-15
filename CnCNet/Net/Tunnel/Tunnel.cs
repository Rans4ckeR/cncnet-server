using System.Buffers;
using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CnCNetServer;

internal abstract class Tunnel(ILogger logger, IOptions<ServiceOptions> serviceOptions, IHttpClientFactory httpClientFactory)
    : IAsyncDisposable
{
    private const int PingRequestPacketSize = 50;
    private const int PingResponsePacketSize = 12;

    private readonly ConcurrentDictionary<int, int>? pingCounter = new();

    private PeriodicTimer? heartbeatTimer;
    private IPAddress? secondaryIpAddress;

    protected abstract int Version { get; }

    protected abstract int Port { get; }

    protected abstract int MinimumPacketSize { get; }

    protected ILogger Logger { get; } = logger;

    protected IOptions<ServiceOptions> ServiceOptions { get; } = serviceOptions;

    protected bool MaintenanceModeEnabled { get; set; }

    protected ConcurrentDictionary<int, int>? ConnectionCounter { get; } = new();

    protected ConcurrentDictionary<uint, TunnelClient>? Mappings { get; } = new();

    protected Socket? Client { get; private set; }

    public virtual async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        Client = new(SocketType.Dgram, ProtocolType.Udp);
        heartbeatTimer = new(TimeSpan.FromMilliseconds(100));
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        _ = StartHeartbeatAsync(cancellationToken);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        Client.Bind(new IPEndPoint(IPAddress.IPv6Any, Port));

        if (Logger.IsEnabled(LogLevel.Information))
            Logger.LogInfo(FormattableString.Invariant($"V{Version} Tunnel UDP server started on port {Port}."));

        while (!cancellationToken.IsCancellationRequested)
        {
            using IMemoryOwner<byte> memoryOwner = MemoryPool<byte>.Shared.Rent(ServiceOptions.Value.MaxPacketSize);
            Memory<byte> buffer = memoryOwner.Memory[..ServiceOptions.Value.MaxPacketSize];
            var remoteSocketAddress = new SocketAddress(Client.AddressFamily);
            int receivedBytes;

            try
            {
                receivedBytes = await Client.ReceiveFromAsync(buffer, SocketFlags.None, remoteSocketAddress, cancellationToken).ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                await Logger.LogExceptionDetailsAsync(ex, LogLevel.Warning).ConfigureAwait(false);
                continue;
            }

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _ = DoReceiveAsync(
                buffer[..receivedBytes],
                remoteSocketAddress,
                cancellationToken).ConfigureAwait(ConfigureAwaitOptions.None);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        }
    }

    public virtual ValueTask DisposeAsync()
    {
        Client?.Dispose();
        heartbeatTimer?.Dispose();

        return ValueTask.CompletedTask;
    }

    protected virtual void CleanupConnection(TunnelClient tunnelClient)
    {
    }

    protected abstract (uint SenderId, uint ReceiverId) GetClientIds(ReadOnlyMemory<byte> buffer);

    protected abstract bool ValidateClientIds(uint senderId, uint receiverId, ReadOnlyMemory<byte> buffer, SocketAddress socketAddress);

    protected abstract ValueTask HandlePacketAsync(uint senderId, uint receiverId, ReadOnlyMemory<byte> buffer, SocketAddress socketAddress, CancellationToken cancellationToken);

    private static IPAddress? GetPublicIpV6Address()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return GetIpV6Addresses().FirstOrDefault();

        ICollection<(IPAddress IpAddress, PrefixOrigin PrefixOrigin, SuffixOrigin SuffixOrigin)> localIpV6Addresses = [.. GetWindowsIpV6Addresses()];
        (IPAddress IpAddress, PrefixOrigin PrefixOrigin, SuffixOrigin SuffixOrigin) publicIpV6Address = localIpV6Addresses.FirstOrDefault(
            static q => q.PrefixOrigin is PrefixOrigin.RouterAdvertisement && q.SuffixOrigin is SuffixOrigin.LinkLayerAddress);

        if (publicIpV6Address.IpAddress is null)
            publicIpV6Address = localIpV6Addresses.FirstOrDefault(static q => q.PrefixOrigin is PrefixOrigin.Dhcp && q.SuffixOrigin is SuffixOrigin.OriginDhcp);

        return publicIpV6Address.IpAddress;
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<(IPAddress IpAddress, PrefixOrigin PrefixOrigin, SuffixOrigin SuffixOrigin)> GetWindowsIpV6Addresses()
        => GetIpV6UnicastAddresses()
        .Select(static q => (q.Address, q.PrefixOrigin, q.SuffixOrigin));

    private static IEnumerable<IPAddress> GetIpV6Addresses()
        => GetIpV6UnicastAddresses()
        .Select(static q => q.Address);

    private static IEnumerable<UnicastIPAddressInformation> GetIpV6UnicastAddresses()
        => NetworkInterface.GetAllNetworkInterfaces()
        .Where(static q => q.OperationalStatus is OperationalStatus.Up)
        .Select(static q => q.GetIPProperties())
        .Where(static q => q.GatewayAddresses.Count is not 0)
        .SelectMany(static q => q.UnicastAddresses)
        .Where(static q => q.Address.AddressFamily is AddressFamily.InterNetworkV6)
        .Where(static q => q.Address is { IsIPv6SiteLocal: false, IsIPv6UniqueLocal: false, IsIPv6LinkLocal: false });

    private async ValueTask ReceiveAsync(ReadOnlyMemory<byte> buffer, SocketAddress socketAddress, CancellationToken cancellationToken)
    {
        (uint senderId, uint receiverId) = GetClientIds(buffer);

        if (Logger.IsEnabled(LogLevel.Debug))
        {
            Logger.LogDebug(
                FormattableString.Invariant($"V{Version} client {socketAddress} ({senderId} -> {receiverId}) received") +
                FormattableString.Invariant($" {buffer.Length} bytes."));
        }
        else if (Logger.IsEnabled(LogLevel.Trace))
        {
            Logger.LogTrace(
                FormattableString.Invariant($"V{Version} client {socketAddress} ({senderId} -> {receiverId}) received") +
                FormattableString.Invariant($" {buffer.Length} bytes: {Convert.ToHexString(buffer.Span)}."));
        }

        if (!ValidateClientIds(senderId, receiverId, buffer, socketAddress))
            return;

        if (await HandlePingRequestAsync(senderId, receiverId, buffer, socketAddress, cancellationToken).ConfigureAwait(false))
            return;

        await HandlePacketAsync(senderId, receiverId, buffer, socketAddress, cancellationToken).ConfigureAwait(false);

        if (Logger.IsEnabled(LogLevel.Debug))
            Logger.LogDebug(FormattableString.Invariant($"V{Version} client {socketAddress} message handled."));
    }

    private async ValueTask<bool> HandlePingRequestAsync(
        uint senderId, uint receiverId, ReadOnlyMemory<byte> buffer, SocketAddress socketAddress, CancellationToken cancellationToken)
    {
        if (senderId is not 0u || receiverId is not 0u)
            return false;

        if (buffer.Length is PingRequestPacketSize)
        {
            if (!IsPingLimitReached(socketAddress))
            {
                if (Logger.IsEnabled(LogLevel.Debug))
                {
                    Logger.LogDebug(
                        FormattableString.Invariant($"V{Version} client {socketAddress} replying to ping ") +
                        FormattableString.Invariant($"({pingCounter!.Count}/{ServiceOptions.Value.MaxPingsGlobal})."));
                }
                else if (Logger.IsEnabled(LogLevel.Trace))
                {
                    Logger.LogTrace(
                        FormattableString.Invariant($"V{Version} client {socketAddress} replying to ping ") +
                        FormattableString.Invariant($"({pingCounter!.Count}/{ServiceOptions.Value.MaxPingsGlobal}):") +
                        FormattableString.Invariant($" {Convert.ToHexString(buffer.Span[..PingResponsePacketSize])}."));
                }

                _ = await Client!.SendToAsync(
                        buffer[..PingResponsePacketSize], SocketFlags.None, socketAddress, cancellationToken)
                    .ConfigureAwait(false);

                return true;
            }

            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug(FormattableString.Invariant($"V{Version} client {socketAddress} ping request ignored:") +
                                FormattableString.Invariant($" ping limit reached."));
            }

            if (Logger.IsEnabled(LogLevel.Warning))
                Logger.LogWarning("Ping limit reached.");
        }
        else if (Logger.IsEnabled(LogLevel.Debug))
        {
            Logger.LogDebug(FormattableString.Invariant($"V{Version} client {socketAddress} ping request ignored:") +
                            FormattableString.Invariant($" invalid packet size {buffer.Length}."));
        }

        return false;
    }

    private async Task DoReceiveAsync(ReadOnlyMemory<byte> buffer, SocketAddress socketAddress, CancellationToken cancellationToken)
    {
        try
        {
            if (buffer.Length < MinimumPacketSize || buffer.Length > ServiceOptions.Value.MaxPacketSize)
            {
                if (Logger.IsEnabled(LogLevel.Debug))
                    Logger.LogDebug(FormattableString.Invariant($"V{Version} Tunnel invalid UDP packet size {buffer.Length}."));

                return;
            }

            await ReceiveAsync(buffer, socketAddress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == cancellationToken)
        {
            // ignore, shut down signal
        }
        catch (Exception ex)
        {
            await Logger.LogExceptionDetailsAsync(ex).ConfigureAwait(false);
        }
    }

    private async ValueTask SendMasterServerHeartbeatAsync(int clients, CancellationToken cancellationToken)
    {
        var path = new Uri(
            FormattableString.Invariant($"?version={Version}&name={Uri.EscapeDataString(ServiceOptions.Value.Name)}") +
            FormattableString.Invariant($"&port={Port}&clients={clients}&maxclients={ServiceOptions.Value.MaxClients}") +
            FormattableString.Invariant($"&masterpw={Uri.EscapeDataString(ServiceOptions.Value.MasterPassword ?? string.Empty)}") +
            FormattableString.Invariant($"&maintenance={(MaintenanceModeEnabled ? 1 : 0)}") +
            FormattableString.Invariant($"&address2={secondaryIpAddress}"),
            UriKind.Relative);
        HttpResponseMessage? httpResponseMessage = null;

        try
        {
            httpResponseMessage = await httpClientFactory.CreateClient(Options.DefaultName)
                .GetAsync(path, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.None);

            string responseContent = await httpResponseMessage.EnsureSuccessStatusCode().Content
                .ReadAsStringAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.None);

            if (!"OK".Equals(responseContent, StringComparison.OrdinalIgnoreCase))
                throw new MasterServerException(responseContent);

            if (Logger.IsEnabled(LogLevel.Information))
                Logger.LogInfo(FormattableString.Invariant($"V{Version} Tunnel Heartbeat sent."));
        }
        catch (HttpRequestException ex)
        {
            await Logger.LogExceptionDetailsAsync(ex, LogLevel.Error, httpResponseMessage).ConfigureAwait(false);
        }
        catch (HttpIOException ex)
        {
            await Logger.LogExceptionDetailsAsync(ex, LogLevel.Error, httpResponseMessage).ConfigureAwait(false);
        }
        finally
        {
            httpResponseMessage?.Dispose();
        }
    }

    private bool IsPingLimitReached(SocketAddress socketAddress)
    {
        if (pingCounter!.Count >= ServiceOptions.Value.MaxPingsGlobal)
            return true;

        int hashCode = socketAddress.GetHashCode();

        if (pingCounter.TryGetValue(hashCode, out int count) && count >= ServiceOptions.Value.MaxPingsPerIp)
            return true;

        pingCounter[hashCode] = ++count;

        return false;
    }

    private async Task StartHeartbeatAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (ServiceOptions.Value is { AnnounceIpV4: true, AnnounceIpV6: true })
                    secondaryIpAddress = GetPublicIpV6Address();

                var masterAnnounceInterval = TimeSpan.FromSeconds(ServiceOptions.Value.MasterAnnounceInterval);

                while (await heartbeatTimer!.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (heartbeatTimer.Period != masterAnnounceInterval)
                        heartbeatTimer.Period = masterAnnounceInterval;

                    int clients = CleanupConnections();

                    if (!ServiceOptions.Value.NoMasterAnnounce)
                        await SendMasterServerHeartbeatAsync(clients, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken == cancellationToken)
            {
                // ignore, shut down signal
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionDetailsAsync(ex).ConfigureAwait(false);
            }
        }
    }

#if EnableLegacyVersion
#pragma warning disable SA1202 // Elements should be ordered by access
    protected virtual int CleanupConnections()
#pragma warning restore SA1202 // Elements should be ordered by access
#else
    private int CleanupConnections()
#endif
    {
        foreach (KeyValuePair<uint, TunnelClient> mapping in Mappings!.Where(static x => x.Value.TimedOut))
        {
            CleanupConnection(mapping.Value);
            _ = Mappings!.Remove(mapping.Key, out _);

            if (Logger.IsEnabled(LogLevel.Information))
            {
                Logger.LogInfo(
                    FormattableString.Invariant($"Removed V{Version} client from ") +
                    FormattableString.Invariant($"{mapping.Value.RemoteIpEndPoint?.ToString() ?? "(not connected)"}, "));
            }

            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug(
                    FormattableString.Invariant($"{Mappings!.Count} clients from {Mappings.Values
                        .Select(static q => q.RemoteSocketAddress).Where(static q => q is not null).Distinct().Count()} IPs."));
            }
        }

        pingCounter!.Clear();

        return Mappings!.Count;
    }
}