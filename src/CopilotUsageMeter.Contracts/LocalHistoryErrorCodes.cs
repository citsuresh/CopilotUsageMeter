namespace CopilotUsageMeter.Contracts
{
    public static class LocalHistoryErrorCodes
    {
        // Normal states, not problems: nothing recorded yet, no turns, or the chat has no record.
        public static bool IsInformational(string? code) =>
            code == "history_not_recorded" || code == "history_no_turns" || code == "history_session_unavailable";

        // Retrying cannot fix these on its own; the user must install or reconfigure the tool.
        public static bool IsPermanent(string? code) =>
            code == "history_tool_missing" || code == "history_invalid_configuration";

        // True when a local read failed in a way that is worth retrying automatically.
        public static bool IsRetryableFailure(string status, string? code) =>
            status != "available" && !IsInformational(code) && !IsPermanent(code);
    }
}
