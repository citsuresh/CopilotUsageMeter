using System;
using System.Reflection;

namespace CopilotUsageMeter.Panel.Core
{
    public static class ActiveChatIdentity
    {
        public static string? Read(object? context)
        {
            if (context == null) return null;
            var type = context.GetType();
            var member = type.Name == "HarnessSessionViewModel" ? "SessionId"
                : type.Name == "HarnessChatViewModel" ? "DisplayedSessionId" : null;
            if (member == null) return null;
            var value = ReadMember(context, member);
            if (value == null)
            {
                var owner = ReadMember(context, type.Name == "HarnessSessionViewModel" ? "Session" : "Router");
                value = ReadMember(owner, member);
            }
            return Guid.TryParseExact(value?.ToString(), "D", out var id) ? id.ToString("D") : null;
        }

        private static object? ReadMember(object? owner, string member)
        {
            if (owner == null) return null;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var type = owner.GetType();
            var property = type.GetProperty(member, flags);
            return property != null ? property.GetValue(owner) : type.GetField(member, flags)?.GetValue(owner);
        }
    }
}