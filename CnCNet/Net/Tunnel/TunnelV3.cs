using System.Security.Cryptography;
using System.Text;

namespace CnCNetServer;

internal sealed class TunnelV3(ILogger<TunnelV3> logger, IOptions<ServiceOptions> options, IHttpClientFactory httpClientFactory)
    : Tunnel(logger, options, httpClientFactory)
{
    private const int PlayerIdSize = sizeof(int);
    private const int TunnelCommandSize = 1;
    private const int TunnelCommandHashSize = 20;
    private const int TunnelCommandRequestPacketSize = (PlayerIdSize * 2) + TunnelCommandSize + TunnelCommandHashSize;
    private const double CommandRateLimitInSeconds = 60d;

    private byte[]? maintenancePasswordSha1;
    private long lastCommandTick;

    private enum TunnelCommand : byte
    {
        MaintenanceMode
    }

    protected override int Version => 3;

    protected override int Port => ServiceOptions.Value.TunnelPort;

    protected override int MinimumPacketSize => 8;

    public override ValueTask StartAsync(CancellationToken cancellationToken)
    {
        if (ServiceOptions.Value.MaintenancePassword?.Length is not null and not 0)
#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
            maintenancePasswordSha1 = SHA1.HashData(Encoding.UTF8.GetBytes(ServiceOptions.Value.MaintenancePassword));
#pragma warning restore CA5350 // Do Not Use Weak Cryptographic Algorithms

        lastCommandTick = DateTime.UtcNow.Ticks;

        return base.StartAsync(cancellationToken);
    }

    protected override void CleanupConnection(TunnelClient tunnelClient)
    {
        int hashCode = tunnelClient.RemoteSocketAddress!.GetHashCode();

        if (--ConnectionCounter[hashCode] <= 0)
            _ = ConnectionCounter.Remove(hashCode, out _);
    }

    protected override (uint SenderId, uint ReceiverId) GetClientIds(ReadOnlyMemory<byte> buffer)
    {
        uint senderId = BitConverter.ToUInt32(buffer[..PlayerIdSize].Span);
        uint receiverId = BitConverter.ToUInt32(buffer[PlayerIdSize..(PlayerIdSize * 2)].Span);

        return (senderId, receiverId);
    }

    protected override bool ValidateClientIds(uint senderId, uint receiverId, ReadOnlyMemory<byte> buffer, SocketAddress socketAddress)
    {
        var endPoint = (IPEndPoint)new IPEndPoint(0L, 0).Create(socketAddress);

        if (senderId is 0u)
        {
            if (receiverId is uint.MaxValue && buffer.Length >= TunnelCommandRequestPacketSize)
                ExecuteCommand((TunnelCommand)buffer.Span[(PlayerIdSize * 2)..((PlayerIdSize * 2) + TunnelCommandSize)][0], buffer, endPoint);

            if (receiverId is not 0u)
                return false;
        }

        // ReSharper disable once InvertIf
        if ((senderId == receiverId && senderId is not 0u) || IPAddress.IsLoopback(endPoint.Address) || endPoint.Address.Equals(IPAddress.Broadcast)
            || endPoint.Address.Equals(IPAddress.Any) || endPoint.Address.Equals(IPAddress.IPv6Any) || endPoint.Port is 0)
        {
            if (Logger.IsEnabled(LogLevel.Debug))
                Logger.LogDebug(FormattableString.Invariant($"V{Version} client {endPoint} invalid endpoint."));

            return false;
        }

        return true;
    }

    protected override ValueTask HandlePacketAsync(
        uint senderId, uint receiverId, ReadOnlyMemory<byte> buffer, SocketAddress socketAddress, CancellationToken cancellationToken)
    {
        if (Mappings.TryGetValue(senderId, out TunnelClient? sender))
        {
            if (!HandleExistingClient(socketAddress, sender))
                return ValueTask.CompletedTask;
        }
        else
        {
            sender = HandleNewClient(senderId, socketAddress);
        }

        if (Mappings.TryGetValue(receiverId, out TunnelClient? receiver) && !receiver.RemoteSocketAddress!.Equals(sender.RemoteSocketAddress))
            return ForwardPacketAsync(senderId, receiverId, buffer, receiver, cancellationToken);

        if (Logger.IsEnabled(LogLevel.Debug))
        {
            Logger.LogDebug(
                FormattableString.Invariant($"V{Version} client {socketAddress} mapping not found or receiver") +
                FormattableString.Invariant($" {receiver?.RemoteIpEndPoint} equals sender {sender.RemoteIpEndPoint}."));
        }

        return ValueTask.CompletedTask;
    }

    private async ValueTask ForwardPacketAsync(
        uint senderId, uint receiverId, ReadOnlyMemory<byte> buffer, TunnelClient receiver, CancellationToken cancellationToken)
    {
        if (Logger.IsEnabled(LogLevel.Debug))
        {
            Logger.LogDebug(
                FormattableString.Invariant($"V{Version} client {senderId} sending {buffer.Length} bytes to ") +
                FormattableString.Invariant($"{receiver.RemoteIpEndPoint} ({receiverId})."));
        }
        else if (Logger.IsEnabled(LogLevel.Trace))
        {
            Logger.LogTrace(
                FormattableString.Invariant($"V{Version} client ({senderId}) sending {buffer.Length} bytes to ") +
                FormattableString.Invariant($"{receiver.RemoteIpEndPoint} ({receiverId}):  {Convert.ToHexString(buffer.Span)}."));
        }

        _ = await Client!.SendToAsync(buffer, SocketFlags.None, receiver.RemoteSocketAddress!, cancellationToken).ConfigureAwait(false);
    }

    private TunnelClient HandleNewClient(uint senderId, SocketAddress socketAddress)
    {
        TunnelClient sender = new(ServiceOptions.Value.ClientTimeout, socketAddress);

        if (Mappings.Count < ServiceOptions.Value.MaxClients && !MaintenanceModeEnabled && IsNewConnectionAllowed(socketAddress) && Mappings.TryAdd(senderId, sender))
        {
            if (Logger.IsEnabled(LogLevel.Information))
                Logger.LogInfo(FormattableString.Invariant($"New V{Version} client from {sender.RemoteIpEndPoint}."));

            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug(
                    FormattableString.Invariant($"{ConnectionCounter.Values.Sum()} clients from ") +
                    FormattableString.Invariant($"{ConnectionCounter.Count} IPs."));
            }
        }
        else if (Logger.IsEnabled(LogLevel.Information))
        {
            Logger.LogInfo(FormattableString.Invariant($"Denied new V{Version} client from {sender.RemoteIpEndPoint}"));
        }

        return sender;
    }

    private bool HandleExistingClient(SocketAddress socketAddress, TunnelClient sender)
    {
        if (!socketAddress.Equals(sender.RemoteSocketAddress))
        {
            if (sender.TimedOut && !MaintenanceModeEnabled && IsNewConnectionAllowed(socketAddress, sender.RemoteSocketAddress!))
            {
                sender.RemoteSocketAddress = socketAddress;

                if (Logger.IsEnabled(LogLevel.Information))
                    Logger.LogInfo(FormattableString.Invariant($"Reconnected V{Version} client from {sender.RemoteIpEndPoint}."));

                if (Logger.IsEnabled(LogLevel.Debug))
                {
                    Logger.LogDebug(
                        FormattableString.Invariant($"{Mappings.Count} clients from ") +
                        FormattableString.Invariant($"{Mappings.Values.Select(static q => q.RemoteIpEndPoint)
                            .Where(static q => q is not null).Distinct().Count()} IPs."));
                }
            }
            else
            {
                if (Logger.IsEnabled(LogLevel.Debug))
                {
                    Logger.LogDebug(
                        FormattableString.Invariant($"V{Version} client {sender.RemoteIpEndPoint} denied {sender.TimedOut}") +
                        FormattableString.Invariant($" {MaintenanceModeEnabled} {sender.RemoteIpEndPoint}."));
                }

                return false;
            }
        }

        sender.SetLastReceiveTick();

        return true;
    }

    private bool IsNewConnectionAllowed(SocketAddress newSocketAddress, SocketAddress? oldSocketAddress = null)
    {
        int hashCode = newSocketAddress.GetHashCode();

        if (ConnectionCounter.TryGetValue(hashCode, out int count) && count >= ServiceOptions.Value.IpLimit)
            return false;

        if (oldSocketAddress is null)
        {
            ConnectionCounter[hashCode] = ++count;
        }
        else if (!newSocketAddress.Equals(oldSocketAddress))
        {
            ConnectionCounter[hashCode] = ++count;

            int oldIpHash = oldSocketAddress.GetHashCode();

            if (--ConnectionCounter[oldIpHash] <= 0)
                _ = ConnectionCounter.Remove(oldIpHash, out _);
        }

        return true;
    }

    private void ExecuteCommand(TunnelCommand command, ReadOnlyMemory<byte> data, IPEndPoint endPoint)
    {
        if (TimeSpan.FromTicks(DateTime.UtcNow.Ticks - lastCommandTick).TotalSeconds < CommandRateLimitInSeconds
            || maintenancePasswordSha1 is null || ServiceOptions.Value.MaintenancePassword!.Length is 0)
        {
            return;
        }

        lastCommandTick = DateTime.UtcNow.Ticks;

        ReadOnlySpan<byte> commandPasswordSha1 = data.Slice((PlayerIdSize * 2) + TunnelCommandSize, TunnelCommandHashSize).Span;

        if (!commandPasswordSha1.SequenceEqual(maintenancePasswordSha1))
        {
            if (Logger.IsEnabled(LogLevel.Warning))
                Logger.LogWarning(FormattableString.Invariant($"Invalid Maintenance mode request by {endPoint}."));

            return;
        }

        MaintenanceModeEnabled = command switch
        {
            TunnelCommand.MaintenanceMode => !MaintenanceModeEnabled,
            _ => MaintenanceModeEnabled
        };

        if (Logger.IsEnabled(LogLevel.Warning))
            Logger.LogWarning(FormattableString.Invariant($"Maintenance mode set to {MaintenanceModeEnabled} by {endPoint}."));
    }
}