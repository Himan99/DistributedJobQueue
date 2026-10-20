# SPEC-009 — Pool Isolation (Fast / Batch)

**ID:** SPEC-009  
**Title:** Worker pool isolation for fast vs batch job types  
**Status:** Not started  
**Type:** New worker deployment configuration  
**Architecture ref:** Section 2 (diagram), Section 5 — Fast pool vs Batch pool

## Problem
No pool isolation exists. All job types share the same worker pool, so slow batch jobs can block fast notification jobs behind them.

## Requirements
1. **Fast pool** — handles `email`, `sms` (short-lived, I/O-bound):
   - High replica count (e.g., `docker compose --scale fast-worker=3`)
   - Small thread pool per instance (`POOL_SIZE=20`)
   - Short lease (`LEASE_SECONDS=20`)
2. **Batch pool** — handles `report`, `ach` (longer, CPU-bound):
   - Fewer replicas (e.g., `docker compose --scale batch-worker=2`)
   - Small thread pool per instance (`POOL_SIZE=4`)
   - Longer lease with active heartbeating (`LEASE_SECONDS` higher, `HEARTBEAT_INTERVAL` configured)
3. Each pool configured via environment variables (`ALLOWED_TYPES`, `POOL_SIZE`, `LEASE_SECONDS`, `HEARTBEAT_INTERVAL`).
4. Fast jobs must not queue behind batch jobs — pool isolation via `ALLOWED_TYPES` filtering.

## Files affected
- `docker-compose.yaml` — add `fast-worker` and `batch-worker` services referencing the worker image
- Worker project — env var configuration (already covered in SPEC-006)
- Worker project — entrypoint reads `ALLOWED_TYPES` to filter leased job types

## Verification
- Fast pool workers only lease `email`/`sms` jobs; batch pool workers only lease `report`/`ach` jobs
- Firing 20+ mixed jobs simultaneously: fast jobs complete without waiting for batch jobs
- `docker compose --scale` works for each pool independently
