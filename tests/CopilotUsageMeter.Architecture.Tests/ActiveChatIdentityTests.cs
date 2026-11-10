using CopilotUsageMeter.Panel.Core;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class ActiveChatIdentityTests
{
    private const string Id = "00000000-0000-0000-0000-000000000001";

    [Fact]
    public void ReadsOnlyApprovedMembersWhetherFieldsOrProperties()
    {
        Assert.Equal(Id, ActiveChatIdentity.Read(new Fields.HarnessSessionViewModel(Id)));
        Assert.Equal(Id, ActiveChatIdentity.Read(new Fields.HarnessChatViewModel(Id)));
        Assert.Equal(Id, ActiveChatIdentity.Read(new Properties.HarnessSessionViewModel { SessionId = Id }));
        Assert.Equal(Id, ActiveChatIdentity.Read(new Properties.HarnessChatViewModel { DisplayedSessionId = Id }));
    }

    [Fact]
    public void MissingMalformedOrUnapprovedContextsStayUnavailable()
    {
        Assert.Null(ActiveChatIdentity.Read(null));
        Assert.Null(ActiveChatIdentity.Read(new { SessionId = Id }));
        Assert.Null(ActiveChatIdentity.Read(new Properties.HarnessSessionViewModel()));
        Assert.Null(ActiveChatIdentity.Read(new Properties.HarnessSessionViewModel { SessionId = "001" }));
    }

    [Fact]
    public void ReadsOnlyTheSessionAndRouterIdentityBridges()
    {
        Assert.Equal(Id, ActiveChatIdentity.Read(new Properties.HarnessSessionViewModel
        {
            Session = new BackendSession { SessionId = new OpaqueId(Id) }
        }));
        Assert.Equal(Id, ActiveChatIdentity.Read(new Properties.HarnessChatViewModel
        {
            Router = new RoutingModel { DisplayedSessionId = new OpaqueId(Id) }
        }));
        Assert.Null(ActiveChatIdentity.Read(new Properties.HarnessSessionViewModel { Session = new BackendSession() }));
        Assert.Null(ActiveChatIdentity.Read(new Properties.HarnessChatViewModel
        {
            Router = new RoutingModel { DisplayedSessionId = new OpaqueId("invalid") }
        }));
    }

    private sealed class BackendSession
    {
        public OpaqueId? SessionId { get; set; }
        public string Title => throw new InvalidOperationException("Must not read content.");
    }

    private sealed class RoutingModel
    {
        public OpaqueId? DisplayedSessionId { get; set; }
        public string CurrentSessionId => throw new InvalidOperationException("Unapproved identity.");
    }

    private sealed class OpaqueId(string value)
    {
        public override string ToString() => value;
    }

    private static class Fields
    {
        internal sealed class HarnessSessionViewModel
        {
            private readonly string SessionId;
            public HarnessSessionViewModel(string id) => SessionId = id;
        }
        internal sealed class HarnessChatViewModel
        {
            private readonly string DisplayedSessionId;
            public HarnessChatViewModel(string id) => DisplayedSessionId = id;
        }
    }

    private static class Properties
    {
        internal sealed class HarnessSessionViewModel
        {
            public string? SessionId { get; set; }
            public BackendSession? Session { get; set; }
        }
        internal sealed class HarnessChatViewModel
        {
            public string? DisplayedSessionId { get; set; }
            public RoutingModel? Router { get; set; }
        }
    }
}