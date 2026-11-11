using System.IO;

namespace CopilotUsageMeter.Client
{
    // The tray did not answer in time. It is an IOException so existing handlers still treat it as "not acknowledged".
    public sealed class CommandTimeoutException : IOException
    {
        public string CommandType { get; }

        public CommandTimeoutException(string commandType) : base("The tray did not answer the command in time.")
        {
            CommandType = commandType;
        }
    }
}
