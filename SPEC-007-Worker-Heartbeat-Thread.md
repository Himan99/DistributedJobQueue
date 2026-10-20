# SPEC-007 — Worker Heartbeat Thread

**ID:** SPEC-007  
**Title:** Worker heartbeat thread for lease extension  
**Status:** Not started  
**Type:** New feature within worker service  
**Architecture ref:** Section 5 — `start_heartbeat_thread(job.id, interval=10)`

## Problem
No heartbeat mechanism exists in the worker. A long-running job risks lease expiry, causing the broker to re-lease it to another worker while the first worker is still executing it.

## Requirements
1. Background heartbeat thread per running job, sending `POST /jobs/{id}/heartbeat` at `HEARTBEAT_INTERVAL`.
2. `HEARTBEAT_INTERVAL` must be well under `LEASE_SECONDS` (e.g., 10s heartbeat for 20s lease).
3. Heartbeat stops when the job completes (ack/nack received) or the worker shuts down.
4. Heartbeat failures are handled gracefully (log and continue; do not crash the worker).
5. Heartbeat extends `lease_expires_at` only if `leased_by` matches the current worker (ownership guard already in `PostgresStore.HeartBeat`).

## Files affected
- Worker project — add heartbeat service/thread (e.g., `HeartbeatService.cs`)
- Integrates with `WorkerLoop.cs` lifecycle (start on lease, stop on completion)

## Verification
- A job running longer than the original lease duration completes without being re-leased to another worker (heartbeat keeps it alive)
- Heartbeat stops after the job is acked or nacked
- Worker does not crash if the broker is temporarily unreachable during a heartbeat
