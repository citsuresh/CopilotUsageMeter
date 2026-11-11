# Usage protocol v1

The protocol carries separate account/local state over a same-user local named pipe. Titles and message text are never transmitted. The approved opaque active-session identifier is request-only, held in memory per connection; it is never included in snapshots, logs, settings, exports or network requests. Account network access belongs to the tray's separate GitHub reader, never the transport.

## Connection and framing

The tray owns the readers; Visual Studio clients subscribe without starting additional readers. The Phase 2 invented-data engine remains a test fixture, not a substitute for production source failures.

- Endpoint: `CopilotUsageMeter.v1.<current user SID>`, local machine only. The Tray creates the pipe with a protected DACL granting only that user access and explicitly denying network-logon access. At most 16 connections, including handshakes, are accepted. This same-user boundary is not protection against other applications running as that user.
- Bidirectional UTF-8 JSON objects, one LF-terminated frame per line. Maximum **65,536 bytes excluding LF**, enforced before deserialization. Invalid UTF-8, malformed/incomplete JSON and oversized frames disconnect the sender and produce a generic host diagnostic, never an echo of received content.
- Every envelope has lower-camel-case `protocol` (integer, currently 1) and `type` (case-sensitive string). Unknown fields are ignored. Snapshot DTO members retain their PascalCase contract names. DateTimeOffset values use the DataContract JSON representation, not an ISO string.
- Hello must complete within 10 seconds. Writes are bounded to 5 seconds; cancellation after a write begins closes the connection to avoid reusing a partial frame. All accept/read/retry work is cancelled and streams closed during shutdown. Clients retry with 1/2/4/8/10-second backoff; a protocol-major mismatch reports which components to update and stops automatic retry.

## Messages

| Request | Response / behavior |
| --- | --- |
| `Hello` | `HelloAck`; must be the first message. A major mismatch returns `Error` / `version_mismatch` and closes. |
| `Subscribe` | `Snapshot`, then coalesced `Update` frames (at most one push per 2 seconds). Repeat to resynchronize; a changed context queues the shared local scheduler. |
| `SetActiveSession` with `activeSessionId` (GUID or null), optional `repositoryKey` (public GitHub owner/repo), nonnegative `contextVersion` | Select this connection's context. Clears previous chart/repository values, sends a projected `Snapshot` and `Ack`. Invalid/superseded contexts return a fixed diagnostic without echoing input. Changes share the 1.5-second debounce/5-second throttle. |
| `Refresh` | `Ack` or `Error`; shared single-flight local/account manual refresh, including while paused. Local 5-second throttle and account cooldown/server backoff still apply. |
| `SetPaused` with explicit `paused` boolean | `Ack` or `Error`; suspend/resume automatic local and account updates. Resume catches up. |
| `OpenStatus` or `SignIn` | Open/activate tray status and account controls. No extra reader or implicit browser launch. |
| `BeginSignIn` | Explicitly launch GitHub CLI browser/device sign-in. No token is transported. |
| `ConnectAccount` with `accountLogin` | Import the named secure credential (a blank login imports the single `gh:github.com:<login>` entry; none or several is an error) and schedule profile/quota validation. No plaintext fallback. |
| `SignOut` or `SkipAccount` | Clear this application's authorization and account data, not GitHub CLI's login or local analytics. Storage failures are explicit. |
| `Bye` or EOF | Connection closes. |

Commands carry a client-generated `requestId`; `Ack`/`Error` echoes it. Client commands time out after 10 seconds; a slow shared refresh can finish later. A timeout/disconnect is not proof that a command was not applied; pause is idempotent. The demo-only `SetSettings` command is no longer supported by the production host; its optional DTO remains for fixture/legacy compatibility. Unknown commands/invalid inputs return readable errors. The handshake, not a UI version string, is the compatibility check.

`Subscribe` also carries the client's current opaque identifier and context version for reconnect/resynchronization. A numeric `contextVersion` accompanies state envelopes; clients discard late states from a previous selection after accounting for delivery sequence. Each connection has its own context, so two IDE instances viewing different chats never receive each other's chart. Duplicate active identifiers share the tray's read. Disconnect removes that connection's context; the client retains it only in memory for reconnect. There is no identifier field on `UsageSnapshot`, `LocalUsage`, or current-chat DTOs.

`Local.CurrentChat` defaults to unavailable for legacy/null objects. Deserialization requires nonempty valid turn rows and `AsOfUtc` for status `available`. Missing/null/blank status becomes unavailable; explicit other statuses retain their reason but clear rows/freshness. Empty/null rows, null entries, negative/duplicate indices and negative credits invalidate the entire chart instead of silently dropping usage. Valid nullable credits/timestamps stay unknown, never zero or an invented time. The tracker retains the newest 20 indices; turn timestamps mean the last usage-event instant, not prompt/completion or refresh time.

## Ordering and state

- Every connection starts its snapshot/update `sequence` at 1 and increments by exactly one for each delivered state frame. Command acknowledgements do not consume sequence numbers. Each client has its own delivery sequence.
- `snapshot.HostInstanceId` changes on tray restart. `snapshot.Revision` is host-global and monotonic within that instance. Intentional coalescing or other clients' commands can skip **revisions**, never delivery sequences; that does not indicate data loss.
- A gap in delivery sequence or an unexpected host identity causes Subscribe/resynchronization, not arithmetic on missed values. Reconnecting always repeats Hello/Subscribe and accepts a fresh instance/revision/sequence.
- Production payloads are `IsDemo=false`. `Account` contains independent status, unit, nullable reported figures, freshness/next-sync/reset and optional verified login/source/error metadata. `Local` contains its own status/freshness, UTC-month bounds, exact recorded repository key (not a path), nullable local credits, token counts and the identifier-free chart. Never sum or reconcile the sources; null is unknown, not zero.
- On disconnect, clients retain the last snapshot but label it stale. Pause retains values and timestamps. All clients share the one tray engine; opening another VS window must not create another data reader.

Repository context and account login are command-only fields; neither is echoed in an envelope. Repository keys may label the local snapshot; chat identity never does. Per-client projection does not trigger per-client monthly or account reads. The host/client implementations and tests, not this outline alone, establish what currently works. Broad runtime/plan compatibility is not promised.

## Additive: `CurrentChat.TotalCredits` (2026-11-12)

`Local.CurrentChat.TotalCredits` (nullable decimal) is the whole-chat total, which is not limited to the newest 20 retained turns. Legacy payloads omit it (null, and clients fall back to summing known turn credits). A negative value deserializes to null, and it is cleared with the turns whenever the chat is unavailable. It carries no title, text or identifier; the opaque session id remains request-only.
