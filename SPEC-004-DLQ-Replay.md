# SPEC-004 — DLQ Replay

**ID:** SPEC-004  
**Title:** Dead-lettered job replay endpoint  
**Status:** Implemented  
**Type:** New API endpoint + persistence method  
**Architecture ref:** Section 3 — `POST /jobs/{id}/replay`

## Problem
No way to recover dead-lettered jobs. Once a job reaches `max_retries` and is dead-lettered, it stays there permanently with no manual recovery path.

## Requirements
1. `POST /jobs/{id}/replay` — resets a dead-lettered job back to `queued`:
   - `status = 'queued'`
   - `attempts = 0`
   - `last_error = NULL`
   - `leased_by = NULL`
   - `lease_expires_at = NULL`
   - `run_after = NULL` (immediately eligible)
2. Returns `404` if job not found.
3. Returns `400`/`409` if job is not in `dead_lettered` status.

## Files affected
- `DistributedJobQueue.Api/Controllers/JobsController.cs` — add `ReplayJob(Guid id)` endpoint
- `BrokerService/Persistence/IJobStore.cs` — add `Task Replay(Guid jobId)` method
- `BrokerService/Persistence/PostgresStore.cs` — implement replay UPDATE SQL

## Verification
- Replay on a dead-lettered job → job becomes `queued` with `attempts = 0`, immediately leasable
- Replay on a non-dead-lettered job → `400`/`409` error, job unchanged
- Replay on non-existent job id → `404`
