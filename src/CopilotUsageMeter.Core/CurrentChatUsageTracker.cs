using System;
using System.Collections.Generic;
using System.Linq;
using CopilotUsageMeter.Contracts;

namespace CopilotUsageMeter.Core
{
    public sealed class CurrentChatUsageTracker
    {
        private const int MaximumTurnCount = 20;
        private readonly object gate = new object();
        private string? activeSessionId;
        private CurrentChatUsage currentChat = CreateUnavailable();

        public void SetActiveSession(string? sessionId)
        {
            var normalizedSessionId = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId;
            lock (gate)
            {
                if (string.Equals(activeSessionId, normalizedSessionId, StringComparison.Ordinal)) return;
                activeSessionId = normalizedSessionId;
                currentChat = CreateUnavailable();
            }
        }

        public bool ReplaceTurns(string? sessionId, IEnumerable<CurrentChatTurnUsage> turns, DateTimeOffset asOfUtc, decimal? totalCredits = null)
        {
            if (totalCredits is < 0) throw new ArgumentOutOfRangeException(nameof(totalCredits), "Total credits cannot be negative.");
            if (string.IsNullOrWhiteSpace(sessionId)) return false;
            if (turns is null) throw new ArgumentNullException(nameof(turns));

            var seenTurnIndexes = new HashSet<int>();
            var orderedTurns = new List<CurrentChatTurnUsage>();
            foreach (var turn in turns)
            {
                if (turn is null) throw new ArgumentException("Turn entries cannot be null.", nameof(turns));
                if (turn.TurnIndex < 0) throw new ArgumentOutOfRangeException(nameof(turns), "Turn indexes cannot be negative.");
                if (turn.Credits is < 0) throw new ArgumentOutOfRangeException(nameof(turns), "Turn credits cannot be negative.");
                if (!seenTurnIndexes.Add(turn.TurnIndex)) throw new ArgumentException("Turn indexes must be unique.", nameof(turns));

                orderedTurns.Add(CloneTurn(turn));
            }

            orderedTurns.Sort((left, right) => left.TurnIndex.CompareTo(right.TurnIndex));
            if (orderedTurns.Count > MaximumTurnCount)
                orderedTurns = orderedTurns.Skip(orderedTurns.Count - MaximumTurnCount).ToList();

            lock (gate)
            {
                if (!string.Equals(sessionId, activeSessionId, StringComparison.Ordinal)) return false;
                currentChat = orderedTurns.Count == 0
                    ? CreateUnavailable()
                    : new CurrentChatUsage
                    {
                        Status = "available",
                        AsOfUtc = asOfUtc.ToUniversalTime(),
                        TotalCredits = totalCredits,
                        Turns = orderedTurns.ToArray()
                    };
                return true;
            }
        }

        public CurrentChatUsage GetSnapshot()
        {
            lock (gate) return CloneUsage(currentChat);
        }

        private static CurrentChatUsage CreateUnavailable() => new CurrentChatUsage
        {
            Status = "unavailable",
            Turns = Array.Empty<CurrentChatTurnUsage>()
        };

        private static CurrentChatUsage CloneUsage(CurrentChatUsage usage) => new CurrentChatUsage
        {
            Status = usage.Status,
            AsOfUtc = usage.AsOfUtc,
            TotalCredits = usage.TotalCredits,
            Turns = usage.Turns.Select(CloneTurn).ToArray()
        };

        private static CurrentChatTurnUsage CloneTurn(CurrentChatTurnUsage turn) => new CurrentChatTurnUsage
        {
            TurnIndex = turn.TurnIndex,
            Credits = turn.Credits,
            TimestampUtc = turn.TimestampUtc?.ToUniversalTime()
        };
    }
}
