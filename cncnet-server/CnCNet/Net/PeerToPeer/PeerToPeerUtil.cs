﻿namespace CnCNetServer;

using System.Net;
using System.Net.Sockets;

internal sealed class PeerToPeerUtil : IDisposable
{
    private const int CounterResetInterval = 60 * 1000; // Reset counter every X ms
    private const int MaxRequestsPerIp = 20; // Max requests during one CounterResetInterval period
    private const int MaxConnectionsGlobal = 5000; // Max amount of different ips sending requests during one CounterResetInterval period
    private const int StunId = 26262;

    private readonly Dictionary<int, int> connectionCounter = new(MaxConnectionsGlobal);
    private readonly System.Timers.Timer connectionCounterTimer = new(CounterResetInterval);
    private readonly byte[] sendBuffer = new byte[40];
    private readonly SemaphoreSlim connectionCounterSemaphoreSlim = new(1, 1);

    public PeerToPeerUtil()
    {
        new Random().NextBytes(sendBuffer);
        Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((short)StunId)), 0, sendBuffer, 6, 2);
    }

    public Task StartAsync(int listenPort, CancellationToken cancellationToken)
    {
        connectionCounterTimer.Elapsed += (_, _) => _ = ResetConnectionCounterAsync(cancellationToken);
        connectionCounterTimer.Enabled = true;

        return StartReceiverAsync(listenPort, cancellationToken);
    }

    public void Dispose()
    {
        connectionCounterSemaphoreSlim.Dispose();
        connectionCounterTimer.Dispose();
    }

    private async Task ResetConnectionCounterAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            connectionCounterTimer.Enabled = false;

        await connectionCounterSemaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            connectionCounter.Clear();
        }
        finally
        {
            _ = connectionCounterSemaphoreSlim.Release();
        }
    }

    private async Task StartReceiverAsync(int listenPort, CancellationToken cancellationToken)
    {
        using var client = new UdpClient(listenPort);
        byte[] buffer = new byte[64];
        var remoteEp = new IPEndPoint(IPAddress.Any, 0);

        while (!cancellationToken.IsCancellationRequested)
        {
            SocketReceiveFromResult socketReceiveFromResult = await client.Client.ReceiveFromAsync(buffer, SocketFlags.None, remoteEp, cancellationToken).ConfigureAwait(false);

            if (socketReceiveFromResult.ReceivedBytes == 48)
                await ReceiveAsync(client, buffer, remoteEp, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ReceiveAsync(UdpClient client, byte[] buffer, IPEndPoint remoteEp, CancellationToken cancellationToken)
    {
        if (remoteEp.Address.Equals(IPAddress.Loopback) || remoteEp.Address.Equals(IPAddress.Any) || remoteEp.Address.Equals(IPAddress.Broadcast) || remoteEp.Port == 0 || await IsConnectionLimitReachedAsync(remoteEp.Address, cancellationToken).ConfigureAwait(false))
            return;

        if (IPAddress.NetworkToHostOrder(BitConverter.ToInt16(buffer, 0)) != StunId)
            return;

        Array.Copy(remoteEp.Address.GetAddressBytes(), sendBuffer, 4);
        Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((short)remoteEp.Port)), 0, sendBuffer, 4, 2);

        // obfuscate
        for (int i = 0; i < 6; i++)
            sendBuffer[i] ^= 0x20;

        _ = await client.Client.SendToAsync(sendBuffer, SocketFlags.None, remoteEp, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> IsConnectionLimitReachedAsync(IPAddress address, CancellationToken cancellationToken)
    {
        await connectionCounterSemaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (connectionCounter.Count >= MaxConnectionsGlobal)
                return true;

            int ipHash = address.GetHashCode();

            if (connectionCounter.TryGetValue(ipHash, out int count) && count >= MaxRequestsPerIp)
                return true;

            connectionCounter[ipHash] = ++count;

            return false;
        }
        finally
        {
            _ = connectionCounterSemaphoreSlim.Release();
        }
    }
}