using System.Runtime.Serialization;

namespace CopilotUsageMeter.Contracts
{
    public static class UsageProtocol
    {
        public const int Version = 1;
        public const int MaximumMessageBytes = 65536;
        public const string PipePrefix = "CopilotUsageMeter.v1.";
    }

    [DataContract]
    public sealed class ProtocolMessage
    {
        [DataMember(Name = "protocol")] public int Protocol { get; set; } = UsageProtocol.Version;
        [DataMember(Name = "type")] public string Type { get; set; } = "";
        [DataMember(Name = "requestId", EmitDefaultValue = false)] public string? RequestId { get; set; }
        [DataMember(Name = "sequence", EmitDefaultValue = false)] public long Sequence { get; set; }
        [DataMember(Name = "snapshot", EmitDefaultValue = false)] public UsageSnapshot? Snapshot { get; set; }
        [DataMember(Name = "settings", EmitDefaultValue = false)] public UsageSettings? Settings { get; set; }
        [DataMember(Name = "paused", EmitDefaultValue = false)] public bool? Paused { get; set; }
        [DataMember(Name = "code", EmitDefaultValue = false)] public string? Code { get; set; }
        [DataMember(Name = "message", EmitDefaultValue = false)] public string? Message { get; set; }
    }
}