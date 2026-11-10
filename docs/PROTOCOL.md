# Phase 2 demo protocol v1

This is an invented-data transport skeleton, not GitHub/history integration or the final accounting contract. No titles, message text, chat identifiers or paths are transmitted. No network connections are used.

## Connection and framing

Implemented by the Phase 2 tray host and experimental VSIX demo client. All values are invented; production readers and refresh policies are not implemented.

- Endpoint: `CopilotUsageMeter.v1.<current user SID>`, local machine only. The Tray creates the pipe with a protected DACL granting only that user access and explicitly denying network-logon access. At most 16 connections, including handshakes, are accepted. This same-user boundary is not protection against other applications running as that user.
- Bidirectional UTF-8 JSON objects, one LF-terminated frame per line. Maximum **65,536 bytes excluding LF**, enforced before deserialization. Invalid UTF-8, malformed/incomplete JSON and oversized frames disconnect the sender and produce a generic host diagnostic, never an echo of received content.
- Every envelope has lower-camel-case `protocol` (integer, currently 1) and `type` (case-sensitive string). Unknown fields are ignored. Snapshot DTO members retain their PascalCase contract names. DateTimeOffset values use the DataContract JSON representation, not an ISO string.
- Hello must complete within 10 seconds. Writes are bounded to 5 seconds; cancellation after a write begins closes the connection to avoid reusing a partial frame. All accept/read/retry work is cancelled and streams closed during shutdown. Clients retry with 1/2/4/8/10-second backoff; a protocol-major mismatch reports which components to update and stops automatic retry.

## Messages

| Request | Response / behavior |
| --- | --- |
| `Hello` | `HelloAck`; must be the first message. A major mismatch returns `Error` / `version_mismatch` and closes. |
| `Subscribe` | `Snapshot`, then pushed `Update` frames. Repeat Subscribe to resynchronize. No reader work is caused by subscribing. |
| `Refresh` | `Ack` or `Error`; manually refreshes invented values, including while paused. Host coalesces requests arriving within one second. |
| `SetPaused` with explicit `paused` boolean | `Ack` or `Error`; suspends/resumes automatic demo updates. Resume catches up once. |
| `SetSettings` with `settings.DemoRefreshSeconds` | `Ack` or `Error`; integer 5-300 seconds, persisted by the host. This setting controls only the demo timer, not the future independent GitHub/local policies. |
| `OpenStatus` | `Ack` or `Error`; opens/activates the tray's demo status/settings window without starting another engine. |
| `Bye` or EOF | Connection closes. |

Commands carry a client-generated `requestId`; `Ack`/`Error` echoes it. Client commands time out after 10 seconds. A timeout/disconnect is not proof that an already-sent command was not applied; settings and pause are idempotent. Unknown commands and invalid settings return readable errors rather than successful defaults. The socket handshake, not a version string in a UI, is the compatibility check.

## Ordering and state

- Every connection starts its snapshot/update `sequence` at 1 and increments by exactly one for each delivered state frame. Command acknowledgements do not consume sequence numbers. Each client has its own delivery sequence.
- `snapshot.HostInstanceId` changes on tray restart. `snapshot.Revision` is host-global and monotonic within that instance. Intentional coalescing or other clients' commands can skip **revisions**, never delivery sequences; that does not indicate data loss.
- A gap in delivery sequence or an unexpected host identity causes Subscribe/resynchronization, not arithmetic on missed values. Reconnecting always repeats Hello/Subscribe and accepts a fresh instance/revision/sequence.
- Every payload is explicitly `IsDemo=true`. `Account` contains an independent status, reporting unit, optional allowance/consumed/remaining and timestamp. `Local` contains its own status/timestamp, synthetic repository ID, local overall/repository credits and token counts. Never sum or reconcile those sources.
- On disconnect, clients retain the last snapshot but label it stale. Pause retains values and timestamps. All clients share the one tray engine; opening another VS window must not create another data reader.

The host/client command implementations and tests, not this outline alone, establish what currently works. Chat embedding, authentication, toolkit reads and production refresh schedules remain later-phase work.