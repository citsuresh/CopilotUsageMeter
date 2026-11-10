using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CopilotUsageMeter.Contracts;

namespace CopilotUsageMeter.Transport.Pipes
{
    public sealed class PipeConnection : IDisposable
    {
        private readonly Stream stream;
        private readonly SemaphoreSlim writeGate = new SemaphoreSlim(1, 1);
        private readonly byte[] buffer = new byte[4096];
        private int offset;
        private int available;

        public PipeConnection(Stream stream) => this.stream = stream;

        private static DataContractJsonSerializer CreateSerializer() =>
            new DataContractJsonSerializer(typeof(ProtocolMessage),
                new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 4096 });

        public async Task<ProtocolMessage?> ReadAsync(CancellationToken cancellationToken)
        {
            using (var message = new MemoryStream())
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (offset == available)
                    {
                        available = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                        offset = 0;
                        if (available == 0)
                        {
                            if (message.Length != 0) throw new InvalidDataException("Incomplete protocol frame.");
                            return null;
                        }
                    }

                    byte value = buffer[offset++];
                    if (value == 10) break;
                    if (message.Length >= UsageProtocol.MaximumMessageBytes)
                        throw new InvalidDataException("Protocol frame exceeds 65536 bytes.");
                    message.WriteByte(value);
                }

                try
                {
                    new UTF8Encoding(false, true).GetString(message.GetBuffer(), 0, (int)message.Length);
                    message.Position = 0;
                    return CreateSerializer().ReadObject(message) as ProtocolMessage
                        ?? throw new InvalidDataException("Protocol frame must contain an object.");
                }
                catch (SerializationException)
                {
                    throw new InvalidDataException("Invalid protocol JSON.");
                }
                catch (DecoderFallbackException)
                {
                    throw new InvalidDataException("Invalid protocol UTF-8.");
                }
            }
        }

        public async Task WriteAsync(ProtocolMessage message, CancellationToken cancellationToken)
        {
            byte[] bytes;
            using (var payload = new MemoryStream())
            {
                CreateSerializer().WriteObject(payload, message);
                if (payload.Length > UsageProtocol.MaximumMessageBytes)
                    throw new InvalidDataException("Protocol frame exceeds 65536 bytes.");
                payload.WriteByte(10);
                bytes = payload.ToArray();
            }
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                await writeGate.WaitAsync(deadline.Token).ConfigureAwait(false);
                try
                {
                    using (deadline.Token.Register(() => stream.Dispose()))
                    {
                        await stream.WriteAsync(bytes, 0, bytes.Length, deadline.Token).ConfigureAwait(false);
                        await stream.FlushAsync(deadline.Token).ConfigureAwait(false);
                    }
                }
                finally { writeGate.Release(); }
            }
        }

        public void Dispose() => stream.Dispose();
    }
}