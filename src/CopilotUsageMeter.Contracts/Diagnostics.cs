using System;
using System.Text;

namespace CopilotUsageMeter.Contracts
{
    public enum DiagnosticLevel
    {
        Warning,
        Error
    }

    // Receives privacy-safe diagnostic entries. Implementations must not add any other detail.
    public interface IDiagnosticsSink
    {
        void Write(DiagnosticLevel level, string component, string code, string? exceptionDetail);
    }

    // Shared, host-neutral failure logging. Entries carry only a component name, a fixed code and the exception
    // type with its numeric HRESULT. Exception messages, stack traces and data are never recorded because they can
    // contain chat titles, repository paths, response payloads or credentials.
    public static class Diagnostics
    {
        private const int MaxTokenLength = 64;
        private static volatile IDiagnosticsSink? sink;

        public static IDiagnosticsSink? Sink
        {
            get => sink;
            set => sink = value;
        }

        public static void Warning(string component, string code, Exception? exception = null) =>
            Write(DiagnosticLevel.Warning, component, code, exception);

        public static void Error(string component, string code, Exception? exception = null) =>
            Write(DiagnosticLevel.Error, component, code, exception);

        // Never throws: logging must not turn a handled failure into a new one.
        private static void Write(DiagnosticLevel level, string component, string code, Exception? exception)
        {
            try
            {
                sink?.Write(level, Token(component), Token(code), Describe(exception));
            }
            catch (Exception)
            {
            }
        }

        public static string? Describe(Exception? exception)
        {
            if (exception == null) return null;
            var builder = new StringBuilder();
            Append(builder, exception);
            var inner = exception.InnerException;
            if (inner != null)
            {
                builder.Append(" <- ");
                Append(builder, inner);
            }
            return builder.ToString();
        }

        private static void Append(StringBuilder builder, Exception exception) =>
            builder.Append(exception.GetType().FullName).Append(" (0x").Append(exception.HResult.ToString("X8")).Append(')');

        // Restricts caller-supplied labels to a short, fixed alphabet so a mistaken argument cannot smuggle text into the log.
        public static string Token(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "unknown";
            var builder = new StringBuilder(Math.Min(value!.Length, MaxTokenLength));
            foreach (var character in value)
            {
                if (builder.Length >= MaxTokenLength) break;
                builder.Append(char.IsLetterOrDigit(character) && character < 128 || character == '.' || character == '_' || character == '-' ? character : '_');
            }
            return builder.ToString();
        }
    }
}
