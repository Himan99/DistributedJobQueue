# SPEC-010 — Observability & Graceful Shutdown

**ID:** SPEC-010  
**Title:** Worker graceful shutdown, health endpoints, and audit visibility  
**Status:** Proposed  
**Priority:** High  
**Type:** Operational readiness / Observability

---

## Problem

The core queue mechanics are complete (SPEC-001 through SPEC-009), but the system lacks production-grade operational capabilities:

1. **No graceful shutdown** — `WorkerLoop.StopAsync` returns immediately without waiting for in-flight jobs to complete or cancelling their heartbeat threads. A `docker compose down` or rolling update can leave jobs half-processed.
2. **No health / readiness endpoints** — No way for orchestration (Docker, Kubernetes) to know if the broker or workers are healthy.
3. **No audit trail visibility via API** — The `job_attempts` table is written (SPEC-008) but there's no endpoint to query it. DLQ inspection requires raw SQL.
4. **No metrics** — No Prometheus `/metrics` endpoint to observe queue depth, throughput, latency, DLQ size, etc.

---

## Requirements

### 1. Worker Graceful Shutdown
- `WorkerLoop.StopAsync(CancellationToken)` must:
  - Stop accepting new leases (exit the lease loop)
  - Wait for all in-flight jobs to complete (ack/nack) up to a configurable timeout (e.g., `SHUTDOWN_TIMEOUT_SECONDS`, default 60s)
  - Cancel all active `HeartbeatService` tasks for in-flight jobs
  - Log the number of jobs drained vs. abandoned (if timeout exceeded)
  - Return only after drain completes or timeout fires

### 2. Health & Readiness Endpoints (Broker API)
Add to `JobsController` (or a new `HealthController`):
- `GET /health/live` — Liveness: returns `200 OK` if process is running (no DB check)
- `GET /health/ready` — Readiness: returns `200 OK` if DB is reachable and migrations applied; `503` otherwise
- Both return JSON: `{ "status": "healthy" }` or `{ "status": "unhealthy", "reason": "..." }`

### 3. Audit Trail Endpoint
- `GET /jobs/{id}/attempts` — Returns all `job_attempts` rows for a job, ordered by `attempt_num`:
```json
[
  { "attempt_num": 1, "worker_id": "worker-fast-3", "outcome": "failed", "error": "timeout", "started_at": "...", "finished_at": "..." },
  { "attempt_num": 2, "worker_id": "worker-fast-1", "outcome": "succeeded", "error": null, "started_at": "...", "finished_at": "..." }
]
```
- Returns `404` if job not found.

### 4. Prometheus Metrics Endpoint (Broker API)
- `GET /metrics` — Exposes:
  - `jobs_queued_total{type}` — Gauge: current queued count per type
  - `jobs_running_total{type}` — Gauge: current running count per type
  - `jobs_succeeded_total` — Counter: total acked
  - `jobs_dead_lettered_total` — Counter: total dead-lettered
  - `job_lease_duration_seconds` — Histogram: lease-to-ack/nack latency
  - `job_retry_backoff_seconds` — Histogram: configured backoff per nack
  - `worker_pool_size{pool}` — Gauge: configured pool size (from env)
  - `worker_in_flight{pool}` — Gauge: currently executing jobs

Use `prometheus-net.AspNetCore` package.

### 5. Database Index for Audit Queries
- Add index: `CREATE INDEX idx_job_attempts_job_id ON job_attempts(job_id);`

---

## Files Affected

| File | Change |
|------|--------|
| `DistributedJobQueue.Worker/WorkerLoop.cs` | Rewrite `StopAsync` to drain in-flight jobs; add `SHUTDOWN_TIMEOUT_SECONDS` env var |
| `DistributedJobQueue.Worker/HeartbeatService.cs` | Accept external `CancellationToken` for coordinated cancel; `StartAsync` links to it |
| `DistributedJobQueue.Api/Program.cs` | Add `prometheus-net.AspNetCore`; map `/metrics`; add health checks |
| `DistributedJobQueue.Api/Controllers/JobsController.cs` | Add `GetJobAttempts(Guid id)` endpoint |
| `DistributedJobQueue.Api/Controllers/HealthController.cs` (new) | Add `GetLive`, `GetReady` endpoints |
| `BrokerService/Persistence/IJobStore.cs` | Add `Task<List<JobAttempt>> GetAttempts(Guid jobId)` |
| `BrokerService/Persistence/PostgresStore.cs` | Implement `GetAttempts`; add `finished_at` to `Ack` UPDATE |
| `BrokerService/Domain/JobAttempt.cs` (new) | DTO for audit rows |
| `db/JobQueueDbInit.sql` | Add `CREATE INDEX idx_job_attempts_job_id ON job_attempts(job_id);` |
| `docker-compose.yaml` | (Optional) Add `prometheus` + `grafana` services for local dev |

---

## Verification

### Manual Tests
1. **Graceful shutdown:**
   - Start worker with `POOL_SIZE=2`, enqueue 5 slow jobs (handler delays 10s)
   - Send `SIGTERM` (or `docker stop`) — worker should finish all 5 before exiting
   - Verify no job is left in `running` status with expired lease

2. **Health endpoints:**
   - `curl /health/live` → `200 { "status": "healthy" }`
   - Stop Postgres → `curl /health/ready` → `503`
   - Restart Postgres → `curl /health/ready` → `200`

3. **Audit endpoint:**
   - Run a job through failure → retry → success
   - `GET /jobs/{id}/attempts` returns 2 rows with correct outcomes/errors

4. **Metrics:**
   - `curl /metrics` shows all listed metrics with non-zero values after running jobs

### Automated Tests (add to `PostgresStoreTest.cs`)
```csharp
// 18. Ack sets finished_at
private async Task AckSetsFinishedAtTest() { ... }

// 19. GetAttempts returns audit trail
private async Task GetAttemptsReturnsAuditTrailTest() { ... }

// 20. WorkerLoop graceful shutdown drains in-flight jobs
// (Requires WorkerLoop test harness — add to Worker.Tests)
```

### Integration Test (new)
- `WorkerLoopGracefulShutdownTest` in `DistributedJobQueue.Worker.Tests`:
  - Start `WorkerLoop` with slow handlers
  - Call `StopAsync` with timeout
  - Assert all jobs reached `Succeeded` or were re-queued (not stuck in `Running`)

---

## Configuration

| Env Var | Default | Applies To |
|---------|---------|------------|
| `SHUTDOWN_TIMEOUT_SECONDS` | `60` | Worker — max time to wait for in-flight jobs on shutdown |
| `HEALTH_CHECK_DB_TIMEOUT_MS` | `2000` | Broker — DB ping timeout for `/health/ready` |

---

## Notes

- **`finished_at` on Ack:** Currently `PostgresStore.Ack` does not set `jobs.finished_at`. This spec adds it (single-line SQL change) — enables "time to complete" metrics.
- **Heartbeat coordination:** `HeartbeatService.StartAsync` currently creates its own `CancellationTokenSource`. Change to accept a parent token from `WorkerLoop` so shutdown cancels heartbeats atomically.
- **Metrics cardinality:** Keep labels low-cardinality (`type`, `pool`). Do NOT label by `job_id` or `worker_id`.
- **Prometheus package:** `prometheus-net.AspNetCore` + `prometheus-net` (standard .NET library).

---

## Dependencies

- `prometheus-net.AspNetCore` (NuGet)
- `prometheus-net` (NuGet)
- `Microsoft.Extensions.Diagnostics.HealthChecks` (already in ASP.NET Core)

---

## Out of Scope (Future Specs)

- Distributed tracing (OpenTelemetry)
- Structured JSON logging with correlation IDs
- Alerting rules / Grafana dashboards (ops concern)
- Autoscaling based on queue depth (needs metrics first — this spec enables it)