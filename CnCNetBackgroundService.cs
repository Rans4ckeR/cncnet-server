#pragma warning disable CA1812 // Avoid uninstantiated internal classes
namespace CnCNetServer;

// ReSharper disable once SuggestBaseTypeForParameterInConstructor
internal sealed class CnCNetBackgroundService(
    ILogger<CnCNetBackgroundService> logger,
    IOptions<ServiceOptions> options,
    TunnelV3 tunnelV3,
#if EnableLegacyVersion
    TunnelV2 tunnelV2,
#endif
    PeerToPeerUtil peerToPeerUtil1,
    PeerToPeerUtil peerToPeerUtil2) : BackgroundService
{
    public const int ErrorExitCode = 1;

    private const int StunPort1 = 3478;
    private const int StunPort2 = 8054;

    private readonly ILogger logger = logger;
    private readonly IOptions<ServiceOptions> options = options;
#pragma warning disable CA2213 // Disposable fields should be disposed
    private readonly TunnelV3 tunnelV3 = tunnelV3;
#if EnableLegacyVersion
    private readonly TunnelV2 tunnelV2 = tunnelV2;
#endif
    private readonly PeerToPeerUtil peerToPeerUtil1 = peerToPeerUtil1;
    private readonly PeerToPeerUtil peerToPeerUtil2 = peerToPeerUtil2;
#pragma warning restore CA2213 // Disposable fields should be disposed

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInfo(FormattableString.Invariant($"Server {options.Value.Name} starting."));

            await base.StartAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.None);

            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInfo(FormattableString.Invariant($"Server {options.Value.Name} started."));
        }
        catch (Exception ex)
        {
            await logger.LogExceptionDetailsAsync(ex).ConfigureAwait(false);

            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInfo(FormattableString.Invariant($"Server {options.Value.Name} stopping."));

            await base.StopAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.None);

            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInfo(FormattableString.Invariant($"Server {options.Value.Name} stopped."));
        }
        catch (Exception ex)
        {
            await logger.LogExceptionDetailsAsync(ex).ConfigureAwait(false);

            throw;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var tasks = new List<Task>();

            if (options.Value.TunnelV3Enabled)
                tasks.Add(CreateLongRunningTask(() => tunnelV3.StartAsync(stoppingToken), tunnelV3, stoppingToken));
#if EnableLegacyVersion

            if (options.Value.TunnelV2Enabled)
            {
                tasks.Add(CreateLongRunningTask(() => tunnelV2.StartAsync(stoppingToken), tunnelV2, stoppingToken));
                tasks.Add(CreateLongRunningTask(() => tunnelV2.StartHttpServerAsync(stoppingToken), tunnelV2, stoppingToken));
            }
#endif

            if (!options.Value.NoPeerToPeer)
            {
                tasks.Add(CreateLongRunningTask(() => peerToPeerUtil1.StartAsync(StunPort1, stoppingToken), peerToPeerUtil1, stoppingToken));
                tasks.Add(CreateLongRunningTask(() => peerToPeerUtil2.StartAsync(StunPort2, stoppingToken), peerToPeerUtil2, stoppingToken));
            }

            await WhenAllSafe(tasks).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await logger.LogExceptionDetailsAsync(ex).ConfigureAwait(false);
            Environment.Exit(ErrorExitCode);
        }
    }

    private static async ValueTask WhenAllSafe(IEnumerable<Task> tasks)
    {
        var whenAllTask = Task.WhenAll(tasks);

        try
        {
            await whenAllTask.ConfigureAwait(ConfigureAwaitOptions.None);
        }
        catch
        {
            if (whenAllTask.Exception is null)
                throw;

            throw whenAllTask.Exception;
        }
    }

    private Task CreateLongRunningTask(Func<ValueTask> taskCreationFunction, IAsyncDisposable disposable, CancellationToken cancellationToken)
        => Task.Factory.StartNew(
            _ => CreateRestartingTaskAsync(taskCreationFunction, disposable, cancellationToken),
            null,
            cancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();

    private async Task CreateRestartingTaskAsync(Func<ValueTask> taskCreationFunction, IAsyncDisposable disposable, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await taskCreationFunction().ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken == cancellationToken)
            {
                // ignored, shutdown signal
            }
            catch (OperationCanceledException ex)
            {
                await LogExceptionAsync(disposable, ex).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await LogExceptionAsync(disposable, ex).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask LogExceptionAsync(IAsyncDisposable disposable, Exception ex)
    {
        await logger.LogExceptionDetailsAsync(ex).ConfigureAwait(false);
        await disposable.DisposeAsync().ConfigureAwait(false);
    }
}