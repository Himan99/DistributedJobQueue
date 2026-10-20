# SPEC-006 — Worker Service

**ID:** SPEC-006  
**Title:** Worker service with thread pool and handler registry  
**Status:** Not started  
**Type:** New service  
**Architecture ref:** Section 5 — Worker loop, ThreadPoolExecutor, handler registry

## Problem
No worker implementation exists. Only a console tester (`PostgresStoreTest`) exercises the store directly. The architecture requires stateless workers that lease jobs, execute handlers, and ack/nack.

## Requirements
1. Worker loop: `lease → run handler → ack/nack`, repeating with a poll interval when no jobs are available.
2. Thread pool (`ThreadPoolExecutor`) with configurable `POOL_SIZE` for concurrent job execution.
3. Handler registry keyed by job `type` (e.g., `send_card_expiry_reminder`, `generate_payment_report`, `generate_ach_file`).
4. Configuration via environment variables:
   - `ALLOWED_TYPES` — comma-separated list of job types this worker handles
   - `POOL_SIZE` — max concurrent jobs per worker instance
   - `LEASE_SECONDS` — lease duration
   - `HEARTBEAT_INTERVAL` — heartbeat frequency (see SPEC-007)
   - `POLL_INTERVAL` — sleep when no jobs available
5. Workers are stateless and horizontally scalable (no coordination between workers beyond Postgres `SKIP LOCKED`).

## Files to create
- New project: `DistributedJobQueue.Worker/` (console app, net8.0)
- `WorkerLoop.cs` — main lease/run/ack loop
- `HandlerRegistry.cs` — type → handler mapping and dispatch
- `Program.cs` — host builder, DI, env var configuration
- Project reference to `DistributedJobQueue.Core`

## Verification
- Worker leases a job, runs its handler, and acks on success
- Worker nacks on handler failure (job returns to queued or dead-letters after max_retries)
- Multiple worker instances processing the same job type never double-process a job (verified via `SKIP LOCKED`)
- Worker respects `ALLOWED_TYPES` filter
