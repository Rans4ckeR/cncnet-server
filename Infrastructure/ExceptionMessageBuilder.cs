using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace CnCNetServer;

internal static class ExceptionMessageBuilder
{
    extension(Exception ex)
    {
        public string GetDetailedExceptionInfo()
            => new StringBuilder().GetExceptionInfo(ex).ToString();
    }

    extension(HttpResponseMessage httpResponseMessage)
    {
        public async ValueTask<string> GetHttpResponseMessageInfoAsync()
        {
            string content = await httpResponseMessage.Content.ReadAsStringAsync().ConfigureAwait(ConfigureAwaitOptions.None);

            return new StringBuilder()
                .Append(FormattableString.Invariant($"{nameof(HttpResponseMessage)}: {httpResponseMessage}"))
                .AppendLine(FormattableString.Invariant($"{nameof(HttpResponseMessage)}.{nameof(HttpResponseMessage.Content)}: {content}"))
                .ToString();
        }
    }

    extension(StringBuilder sb)
    {
        private StringBuilder GetExceptionInfo(Exception ex)
        {
            sb.AppendLine(FormattableString.Invariant($"{nameof(Exception)}.{nameof(Exception.GetType)}: {ex.GetType()}"))
                .AppendLine(FormattableString.Invariant($"{nameof(Exception)}.{nameof(Exception.Message)}: {ex.Message}"))
                .GetExceptionDetails(ex);

            if (ex is AggregateException aggregateException)
            {
                foreach (Exception innerException in aggregateException.InnerExceptions)
                {
                    _ = sb.AppendLine(FormattableString.Invariant($"{nameof(AggregateException)}.{nameof(AggregateException.InnerExceptions)}:"))
                        .GetExceptionInfo(innerException);
                }
            }
            else if (ex.InnerException is not null)
            {
                _ = sb.AppendLine(FormattableString.Invariant($"{nameof(Exception)}.{nameof(Exception.InnerException)}:"))
                    .GetExceptionInfo(ex.InnerException);
            }

            return sb;
        }

        private void GetExceptionDetails(Exception ex)
            => sb.AppendLine(FormattableString.Invariant($"{nameof(Exception)}.{nameof(Exception.Source)}: {ex.Source}"))
                .AppendLine(FormattableString.Invariant($"{nameof(Exception)}.{nameof(Exception.TargetSite)}: {ex.TargetSite}"))
                .GetHttpRequestExceptionDetails(ex)
                .GetHttpIoExceptionDetails(ex)
                .GetSocketExceptionDetails(ex)
                .GetExternalExceptionDetails(ex)
                .AppendLine(FormattableString.Invariant($"{nameof(Exception)}.{nameof(Exception.StackTrace)}: {ex.StackTrace}"));

        private StringBuilder GetExternalExceptionDetails(Exception ex)
        {
            if (ex is not ExternalException externalException)
                return sb;

            var win32Exception = new Win32Exception(externalException.ErrorCode);

            return sb.AppendLine(FormattableString.Invariant($"{nameof(ExternalException)}.{nameof(ExternalException.ErrorCode)}: {externalException.ErrorCode}"))
                .AppendLine(FormattableString.Invariant($"{nameof(ExternalException)}.{nameof(ExternalException.ErrorCode)} Hex: 0x{externalException.ErrorCode:X8}"))
                .AppendLine(FormattableString.Invariant($"{nameof(Win32Exception)}.{nameof(Exception.Message)}: {win32Exception.Message}"));
        }

        private StringBuilder GetSocketExceptionDetails(Exception ex)
        {
            if (ex is SocketException socketException)
                _ = sb.AppendLine(FormattableString.Invariant($"{nameof(SocketException)}.{nameof(SocketException.SocketErrorCode)}: {socketException.SocketErrorCode}"));

            return sb;
        }

        private StringBuilder GetHttpRequestExceptionDetails(Exception ex)
        {
            if (ex is HttpRequestException httpRequestException)
            {
                _ = sb.AppendLine(FormattableString.Invariant($"{nameof(HttpRequestException)}.{nameof(HttpRequestException.HttpRequestError)}: {httpRequestException.HttpRequestError}"))
                    .AppendLine(FormattableString.Invariant($"{nameof(HttpRequestException)}.{nameof(HttpRequestException.StatusCode)}: {httpRequestException.StatusCode}"));
            }

            return sb;
        }

        private StringBuilder GetHttpIoExceptionDetails(Exception ex)
        {
            if (ex is HttpIOException httpIoException)
                _ = sb.AppendLine(FormattableString.Invariant($"{nameof(HttpIOException)}.{nameof(HttpIOException.HttpRequestError)}: {httpIoException.HttpRequestError}"));

            return sb;
        }
    }
}