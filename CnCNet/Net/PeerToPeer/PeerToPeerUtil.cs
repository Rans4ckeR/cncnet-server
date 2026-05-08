#pragma warning disable CA1812 // Avoid uninstantiated internal classes
using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace CnCNetServer;

internal sealed class PeerToPeerUtil(ILogger<PeerToPeerUtil> logger) : IAsyncDisposable
{
    private const int CounterResetInterval = 60; // Reset counter every X s
    private const int MaxRequestsPerIp = 20; // Max requests during one CounterResetInterval period
    private const int MaxConnectionsGlobal = 5000; // Max amount of different ips sending requests during one CounterResetInterval period
    private const short StunId = 26262;

    private readonly ILogger logger = logger;
    private readonly ConcurrentDictionary<int, int> connectionCounter = new();
    private readonly PeriodicTimer connectionCounterTimer = new(TimeSpan.FromSeconds(CounterResetInterval));

    public ValueTask StartAsync(int listenPort, CancellationToken cancellationToken)
    {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        _ = ResetConnectionCounterAsync(cancellationToken);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

        return StartReceiverAsync(listenPort, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        connectionCounterTimer.Dispose();

        return ValueTask.CompletedTask;
    }

    private static bool IsInvalidRemoteIpEndPoint(IPEndPoint remoteEp) =>
        IPAddress.IsLoopback(remoteEp.Address) || remoteEp.Address.Equals(IPAddress.Broadcast) ||
        remoteEp.Address.Equals(IPAddress.Any) || remoteEp.Address.Equals(IPAddress.IPv6Any) || remoteEp.Port is 0;

    private async Task ResetConnectionCounterAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                while (await connectionCounterTimer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                    connectionCounter.Clear();
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken == cancellationToken)
            {
                // ignore, shut down signal
            }
            catch (Exception ex)
            {
                await logger.LogExceptionDetailsAsync(ex).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask StartReceiverAsync(int listenPort, CancellationToken cancellationToken)
    {
        using var client = new Socket(SocketType.Dgram, ProtocolType.Udp);

        client.Bind(new IPEndPoint(IPAddress.IPv6Any, listenPort));

        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInfo(FormattableString.Invariant($"PeerToPeer UDP server started on port {listenPort}."));

        while (!cancellationToken.IsCancellationRequested)
        {
            IMemoryOwner<byte> memoryOwner = MemoryPool<byte>.Shared.Rent(64);
            bool ownershipTransferred = false;

            try
            {
                Memory<byte> buffer = memoryOwner.Memory[..64];
                var remoteSocketAddress = new SocketAddress(client.AddressFamily);
                int bytesReceived;

                try
                {
                    bytesReceived = await client.ReceiveFromAsync(buffer, SocketFlags.None, remoteSocketAddress, cancellationToken).ConfigureAwait(false);
                }
                catch (SocketException ex)
                {
                    await logger.LogExceptionDetailsAsync(ex, LogLevel.Warning).ConfigureAwait(false);
                    continue;
                }

                if (bytesReceived is 48)
                {
                    ownershipTransferred = true;

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
#pragma warning disable CA2025 // Do not pass 'IDisposable' instances into unawaited tasks
                    _ = ReceiveAsync(client, memoryOwner, remoteSocketAddress, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.None);
#pragma warning restore CA2025 // Do not pass 'IDisposable' instances into unawaited tasks
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                }
            }
            finally
            {
                if (!ownershipTransferred)
                    memoryOwner.Dispose();
            }
        }
    }

    private async Task ReceiveAsync(Socket client, IMemoryOwner<byte> receiveMemoryOwner, SocketAddress remoteSocketAddress, CancellationToken cancellationToken)
    {
        try
        {
            ReadOnlyMemory<byte> receiveBuffer = receiveMemoryOwner.Memory[..48];
            var remoteIpEndpoint = (IPEndPoint)new IPEndPoint(0L, 0).Create(remoteSocketAddress);

            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug(FormattableString.Invariant($"P2P client {remoteIpEndpoint} connected."));

            if (IsInvalidRemoteIpEndPoint(remoteIpEndpoint) || IsConnectionLimitReached(remoteIpEndpoint.Address))
                return;

            if (IPAddress.NetworkToHostOrder(BitConverter.ToInt16(receiveBuffer.Span)) is not StunId)
                return;

            using IMemoryOwner<byte> memoryOwner = MemoryPool<byte>.Shared.Rent(40);
            Memory<byte> sendBuffer = memoryOwner.Memory[..40];

            byte[] addressBytes = remoteIpEndpoint.Address.IsIPv4MappedToIPv6 || remoteIpEndpoint.AddressFamily is AddressFamily.InterNetwork
                ? remoteIpEndpoint.Address.MapToIPv4().GetAddressBytes()
                : remoteIpEndpoint.Address.GetAddressBytes();
            addressBytes.CopyTo(sendBuffer.Span[..addressBytes.Length]);

            byte[] portBytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((short)remoteIpEndpoint.Port));
            portBytes.CopyTo(sendBuffer.Span[addressBytes.Length..(addressBytes.Length + 2)]);

            byte[] stunIdBytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(StunId));
            stunIdBytes.CopyTo(sendBuffer.Span[(addressBytes.Length + 2)..(addressBytes.Length + 4)]);

            // obfuscate
            for (int i = 0; i < addressBytes.Length + portBytes.Length; i++)
                sendBuffer.Span[i] ^= 0x20;

            RandomNumberGenerator.Fill(sendBuffer.Span[(addressBytes.Length + 4)..]);

            _ = await client.SendToAsync(sendBuffer, SocketFlags.None, remoteSocketAddress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == cancellationToken)
        {
            // ignore, shut down signal
        }
        catch (Exception ex)
        {
            await logger.LogExceptionDetailsAsync(ex).ConfigureAwait(false);
        }
        finally
        {
            receiveMemoryOwner.Dispose();
        }
    }

    private bool IsConnectionLimitReached(IPAddress address)
    {
        if (connectionCounter.Count >= MaxConnectionsGlobal)
            return true;

        int hashCode = address.GetHashCode();

        if (connectionCounter.TryGetValue(hashCode, out int count) && count >= MaxRequestsPerIp)
            return true;

        connectionCounter[hashCode] = ++count;

        return false;
    }
}