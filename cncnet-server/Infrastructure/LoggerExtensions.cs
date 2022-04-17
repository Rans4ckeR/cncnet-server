﻿namespace CnCNetServer;

using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

internal static partial class LoggerExtensions
{
    public static async Task LogExceptionDetailsAsync(this ILogger logger, Exception exception, HttpResponseMessage? httpResponseMessage = null)
    {
        logger.LogExceptionDetails(exception);

        if (httpResponseMessage is not null)
            logger.LogException(await httpResponseMessage.GetHttpResponseMessageInfoAsync().ConfigureAwait(false));
    }

    public static void LogExceptionDetails(this ILogger logger, Exception exception)
    {
        logger.LogException(exception.GetDetailedExceptionInfo());
    }

    [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "{message}")]
    public static partial void LogMessage(this ILogger logger, string message);

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "{message}")]
    private static partial void LogException(this ILogger logger, string message);
}