using System;
using System.Threading;
using System.Threading.Tasks;
using CopilotUsageMeter.Contracts;

namespace CopilotUsageMeter.Client
{
    public interface IUsageClient : IDisposable
    {
        event Action<UsageSnapshot>? SnapshotChanged;
        event Action<string>? StatusChanged;
        UsageSnapshot? Snapshot { get; }
        string Status { get; }
        void Start();
        Task<ProtocolMessage> SendAsync(ProtocolMessage command, CancellationToken cancellationToken);
        Task StopAsync();
    }
}