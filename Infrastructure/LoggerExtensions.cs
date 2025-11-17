namespace CnCNetServer;

internal static partial class LoggerExtensions
{
    extension(ILoggingBuilder loggingBuilder)
    {
        public ILoggingBuilder ConfigureLogging()
        {
            using ServiceProvider serviceProvider = loggingBuilder.Services.BuildServiceProvider();

            LogLevel serverLogLevel = serviceProvider.GetRequiredService<IOptions<ServiceOptions>>().Value.ServerLogLevel;
            LogLevel systemLogLevel = serviceProvider.GetRequiredService<IOptions<ServiceOptions>>().Value.SystemLogLevel;

            return loggingBuilder.ConfigureLogging(serverLogLevel, systemLogLevel);
        }

        public ILoggingBuilder ConfigureLogging(LogLevel serverLogLevel, LogLevel systemLogLevel)
            => loggingBuilder
                .SetMinimumLevel(systemLogLevel)
                .AddFilter(nameof(CnCNetServer), serverLogLevel);
    }

    extension(ILogger logger)
    {
        public async ValueTask LogExceptionDetailsAsync(Exception exception, LogLevel logLevel = LogLevel.Error, HttpResponseMessage? httpResponseMessage = null)
        {
            if (!logger.IsEnabled(logLevel))
                return;

            logger.LogException(exception.GetDetailedExceptionInfo(), logLevel);

            if (httpResponseMessage is not null)
                logger.LogException(await httpResponseMessage.GetHttpResponseMessageInfoAsync().ConfigureAwait(false), logLevel);
        }
    }

    [LoggerMessage(EventId = 4, Level = LogLevel.Trace, Message = "{message}")]
    public static partial void LogTrace(this ILogger logger, string message);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "{message}")]
    public static partial void LogDebug(this ILogger logger, string message);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "{message}")]
    public static partial void LogInfo(this ILogger logger, string message);

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "{message}")]
    public static partial void LogWarning(this ILogger logger, string message);

    [LoggerMessage(EventId = 0, Message = "{message}")]
    private static partial void LogException(this ILogger logger, string message, LogLevel logLevel);
}