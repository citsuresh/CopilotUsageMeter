using System;
using System.Globalization;
using System.IO;
using CopilotUsageMeter.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CopilotUsageMeter.Readers.GitHub
{
    public static class GitHubQuotaParser
    {
        public static AccountReadResult Parse(string json, DateTimeOffset asOfUtc)
        {
            try
            {
                var root = ParseObject(json);
                var result = AccountReadResult.Empty("unavailable", "quota-unavailable", "GitHub account quota is unavailable.");
                result.Plan = SafeLabel(root["copilot_plan"]);
                var billing = root["token_based_billing"];
                result.TokenBasedBilling = billing?.Type == JTokenType.Boolean ? billing.Value<bool>() : (bool?)null;
                result.ResetUtc = ParseDate(root["quota_reset_date_utc"]) ?? ParseDate(root["quota_reset_date"]);
                // Structured or unfamiliar billing shapes cannot safely be interpreted as premium-interaction credits.
                if (billing != null && billing.Type != JTokenType.Null && billing.Type != JTokenType.Boolean)
                {
                    result.ErrorCode = "unsupported-billing";
                    result.ErrorMessage = "This GitHub billing format is unsupported.";
                    return result;
                }
                if (!(root["quota_snapshots"] is JObject snapshots) ||
                    !(snapshots["premium_interactions"] is JObject premium))
                {
                    if (result.Plan != null)
                    {
                        result.ErrorCode = "unsupported-plan";
                        result.ErrorMessage = "GitHub does not expose a supported premium-interaction quota for this plan.";
                    }
                    return result;
                }
                var unlimited = Boolean(premium["unlimited"]);
                result.IsUnlimited = unlimited == true;
                if (Boolean(premium["has_quota"]) == false && !result.IsUnlimited) return result;
                result.Usage.Allowance = Number(premium["entitlement"]);
                result.Usage.Consumed = Number(premium["credits_used"]);
                result.Usage.Remaining = Number(premium["remaining"]);
                // Token-based billing is accepted only when the snapshot itself reports finite credits; partial figures are not guessed.
                if (result.TokenBasedBilling == true && !result.IsUnlimited && (result.Usage.Allowance == null || result.Usage.Consumed == null))
                {
                    result.Usage.Allowance = result.Usage.Consumed = result.Usage.Remaining = null;
                    result.ErrorCode = "unsupported-billing";
                    result.ErrorMessage = "This GitHub billing format is unsupported.";
                    return result;
                }
                if (!result.IsUnlimited && result.Usage.Allowance == null && result.Usage.Consumed == null && result.Usage.Remaining == null)
                    return result;
                result.Status = result.Usage.Status = "available";
                result.Usage.AsOfUtc = asOfUtc.ToUniversalTime();
                result.ErrorCode = result.ErrorMessage = null;
                return result;
            }
            catch (JsonException)
            {
                return AccountReadResult.Empty("error", "invalid-response", "GitHub returned an unsupported account response.");
            }
        }

        internal static JObject ParseObject(string json)
        {
            using (var text = new StringReader(json))
            using (var reader = new JsonTextReader(text) { MaxDepth = 32, DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Decimal })
            {
                var result = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read()) throw new JsonReaderException("Unexpected trailing content.");
                return result;
            }
        }

        private static bool? Boolean(JToken? value) => value?.Type == JTokenType.Boolean ? value.Value<bool>() : (bool?)null;
        private static decimal? Number(JToken? value)
        {
            if (value == null || (value.Type != JTokenType.Integer && value.Type != JTokenType.Float)) return null;
            if (!decimal.TryParse(value.ToString(Formatting.None), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || number < 0) return null;
            return number;
        }
        private static DateTimeOffset? ParseDate(JToken? value)
        {
            if (value?.Type == JTokenType.String && DateTimeOffset.TryParse(value.Value<string>(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp)) return timestamp;
            return null;
        }
        private static string? SafeLabel(JToken? token)
        {
            var value = token?.Type == JTokenType.String ? token.Value<string>() : null;
            if (value == null || value.Length > 64) return null;
            foreach (var character in value)
                if (!(character >= 'a' && character <= 'z') && character != '_' && character != '-') return null;
            return value;
        }
    }
}