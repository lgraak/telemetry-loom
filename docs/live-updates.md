# Live sensor updates

Telemetry Loom exposes complete sensor snapshots as a Server-Sent Events stream:

```text
GET /api/sensors/stream
Accept: text/event-stream
```

The server sends one `sensors` event immediately, then sends another at the configured cadence. Each event has a process-local monotonic ID and a versioned JSON payload:

```text
id: 42
event: sensors
data: {"schemaVersion":1,"sequence":42,"capturedAt":"2026-08-03T00:00:00Z","sensors":[...]}
```

Every event contains the complete current sensor collection, including physical and calculated sensors. Consumers should replace their current snapshot rather than merge partial fields. Full snapshots make reconnect behavior deterministic and prevent deleted or unavailable sensors from lingering in consumer state.

The service samples once per interval and broadcasts that same snapshot to every connected client. Each client has a one-snapshot bounded buffer. If a client cannot keep up, it receives the newest state rather than accumulating stale telemetry or forcing faster clients to wait.

## Cadence

The default interval is 1000 milliseconds. Configure it through .NET configuration:

```json
{
  "TelemetryLoom": {
    "LiveUpdates": {
      "IntervalMilliseconds": 1000
    }
  }
}
```

Environment-variable form:

```bash
TelemetryLoom__LiveUpdates__IntervalMilliseconds=500 telemetry-loom
```

Allowed values are 100 through 60000 milliseconds. Invalid values fail startup rather than silently falling back. The cadence is service-wide in this slice; per-client intervals would multiply collection work and are deferred.

## Reconnection contract

Event IDs are monotonic only for the lifetime of one service process. Milestone 5 does not retain history and does not replay `Last-Event-ID`. A reconnect receives a fresh complete snapshot immediately. Consumers should treat service restart or sequence regression as a new stream, not as data loss that can be replayed.

The stream follows the same product access policy as the REST API. It is localhost-only by default and is remotely readable only when an administrator explicitly enables trusted-LAN read-only mode. No separate streaming port, authentication, or TLS behavior is introduced.

## Why SSE

Telemetry is one-way server-to-consumer traffic. SSE provides streaming over ordinary HTTP, automatic browser reconnection, readable diagnostics, and simple clients without adding WebSocket framing or bidirectional session state. WebSockets remain unnecessary until a concrete bidirectional requirement exists.
