# DistributedJobQueue — Project Overview & Reorientation


---

## 1. Architecture: Components & Job Flow

### Project / Folder Map

| Project / Folder | Purpose | Key Files |
|------------------|---------|-----------|
| **BrokerService/** (`DistributedJobQueue.Core`) | Core library: domain models, `IJobStore` interface, Postgres implementation | `Domain/*.cs`, `Persistence/*.cs` |
| **DistributedJobQueue.Api/** | ASP.NET Core Minimal API (REST endpoints) | `Program.cs`, `Controllers/JobsController.cs` |
| **DistributedJobQueue.Worker/** | Worker service: lease → handler → ack/nack loop, thread pool, heartbeat | `WorkerLoop.cs`, `HandlerRegistry.cs`, `HeartbeatService.cs`, `Program.cs` |
| **DistributedJobQueueBrokerService/** | Console tester (runs 17 integration tests as `IHostedService`) | `PostgresStoreTest.cs`, `Program.cs` |
| **DistributedJobQueue.Worker.Tests/** | Worker unit/integration tests | `Program.cs` |
| **db/** | PostgreSQL schema + test reset script | `JobQueueDbInit.sql`, `initdbfortests.sql` |
| **docker-compose.yaml** | Local stack: Postgres 18 + fast-pool (×3) + batch-pool (×2) workers | — |

### Job Lifecycle (Enqueue → Completion/Failure)

```
POST /jobs/save
       │
       ▼
┌──────────────────┐
│ jobs table       │  (status = 'queued', attempts = 0)
│ INSERT with      │
│ idempotency_key  │──▶ UNIQUE constraint dedupes caller retries
└────────┬─────────┘
         │
         ▼ (polling)
POST /jobs/lease ──▶ UPDATE … FOR UPDATE SKIP LOCKED LIMIT 1
       │                 │
       │                 ▼
       │           ┌──────────────────┐
       │           │ status = 'running'│
       │           │ leased_by = worker│
       │           │ lease_expires_at  │
       │           │ attempts += 1     │
       │           │ INSERT job_attempts│
       │           └────────┬─────────┘
       │                    │
       ▼                    ▼
┌──────────────────┐  Worker runs handler(job.payload)
│ LeasedJob DTO    │
└────────┬─────────┘
         │
    ┌────┴────┐
    │         │
    ▼         ▼
 Success   Failure
    │         │
    ▼         ▼
POST       POST
/jobs/ack  /jobs/nack
    │         │
    ▼         ▼
UPDATE     UPDATE jobs
status=    attempts += 1
succeeded  IF attempts >= max_retries:
    │          status = dead_lettered
    │        ELSE:
    ▼          status = queued
job_attempts  run_after = now() + backoff(attempts)  ← exponential + jitter
UPDATE       job_attempts UPDATE (outcome=failed, error)
outcome=      │
succeeded    ▼
             (lease cleared, available for re-lease after run_after)
```

**Key Guarantee:** `FOR UPDATE SKIP LOCKED` in the `Lease` query ensures **no two workers ever lease the same job**. This is the single most important correctness primitive.

---

## 2. Core Guarantees & Implementation Locations

### Leasing (SKIP LOCKED)
- **File:** `BrokerService/Persistence/PostgresStore.cs:138-193` (`Lease` method)
- **Query:** `WITH updated AS (UPDATE jobs SET ... WHERE id = (SELECT id FROM jobs WHERE ... FOR UPDATE SKIP LOCKED LIMIT 1) RETURNING ...) SELECT ... FROM updated WHERE status = 'Running'`
- **Why it works:** `SKIP LOCKED` makes concurrent `UPDATE ... WHERE id = (SELECT ... FOR UPDATE SKIP LOCKED LIMIT 1)` skip rows already locked by another transaction — they simply don't appear in the result.

### At-Least-Once Delivery
| Guarantee | Owner | Mechanism |
|-----------|-------|-----------|
| Job never silently lost | Broker | Durable Postgres row; lease expiry returns orphans to `queued` |
| Job may be delivered >once | Broker (by design) | Worker crashes after `handler()` succeeds but before `ack()` → lease expires → re-leased |
| Job never half-applied without trace | Handler | Handler must be idempotent (check-before-act, or use `idempotency_key`) |
| Duplicate caller submissions deduped | Broker | Unique constraint on `idempotency_key` at `POST /jobs/save` |

**Implementation:** `PostgresStore.Save()` at line 291-326 handles idempotency key unique violation by catching `PostgresException` with `SqlState == UniqueViolation` and returning the existing job ID.

### Retries & Exponential Backoff with Jitter
- **File:** `PostgresStore.cs:71-114` (`Nack` method)
- **SQL (lines 83-86):**
```sql
run_after = CASE
    WHEN attempts + 1 >= max_retries THEN run_after
    ELSE now() + (LEAST(POWER(2, attempts), 60::double precision) + random() * 5) * interval '1 second'
END
```
- **Formula:** `backoff = min(2^attempt, 60) + random(0, 5)` seconds
  - Attempt 0→1: ~1–6s
  - Attempt 1→2: ~2–7s
  - Attempt 2→3: ~4–9s
  - Capped at ~60s + 5s jitter
- **Tests:** `NackExponentialBackoffTest` (line 185), `NackBackoffHasJitterTest` (line 226)

### Idempotency (Caller-side Deduping)
- **File:** `PostgresStore.Save()` lines 310-325
- On `UNIQUE` violation on `idempotency_key`, fetches and returns existing `job_id`
- This is **caller-side** deduping (retries from the submitter), distinct from handler-level idempotency for side effects.

---

## 3. Data Model

### `jobs` Table (Current State)
```sql
CREATE TABLE JOBS (
    ID                UUID PRIMARY KEY DEFAULT GEN_RANDOM_UUID(),
    TYPE              TEXT NOT NULL,
    PAYLOAD           JSONB NOT NULL,
    STATUS            TEXT NOT NULL DEFAULT 'queued',  -- queued | running | succeeded | dead_lettered
    PRIORITY          INT NOT NULL DEFAULT 0,
    ATTEMPTS          INT NOT NULL DEFAULT 0,
    MAX_RETRIES       INT NOT NULL DEFAULT 3,
    IDEMPOTENCY_KEY   TEXT UNIQUE,
    LEASED_BY         TEXT,
    LEASE_EXPIRES_AT  TIMESTAMPTZ,
    RUN_AFTER         TIMESTAMPTZ,
    RESULT_URL        TEXT,
    LAST_ERROR        TEXT,
    CREATED_AT        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    FINISHED_AT       TIMESTAMPTZ
);
CREATE INDEX IDX_JOBS_LEASE ON JOBS (STATUS, TYPE, PRIORITY, CREATED_AT);
```

### `job_attempts` Table (Append-Only Audit History)
```sql
CREATE TABLE job_attempts (
    id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    job_id       UUID REFERENCES jobs(id),
    attempt_num  INT NOT NULL,
    worker_id    TEXT NOT NULL,
    outcome      TEXT NOT NULL,  -- succeeded | failed
    error        TEXT,
    started_at   TIMESTAMPTZ NOT NULL,
    finished_at  TIMESTAMPTZ
);
```

### State Machine
```
queued ──lease──▶ running ──ack──▶ succeeded
                     │
                    nack
                     │
          ┌──────────┴──────────┐
          ▼                     ▼
   attempts < max         attempts >= max
   queued (run_after     dead_lettered
    = now + backoff)           │
                             replay
                                │
                                ▼
                             queued (attempts=0)

Also: running → queued automatically if lease_expires_at < now()
      (handled inline in Lease query: lines 152-155 in PostgresStore)
```

---

## 4. Specs: Implementation Status

| Spec | Title | Status | Evidence |
|------|-------|--------|----------|
| **SPEC-001** | Namespace Alignment | ✅ Implemented | All namespaces `DistributedJobQueue.Core.*` (commit 121de40 → 683029f) |
| **SPEC-002** | Comment/Code Mismatch | ✅ Implemented | Comment in `NackRetryTest` fixed to match SQL (1s not 30s) |
| **SPEC-003** | Get / List Jobs | ✅ Implemented | `JobsController.GetJob`, `ListJobs`; tests `GetJobReturnsExistingJobTest` etc. |
| **SPEC-004** | DLQ Replay | ✅ Implemented | `JobsController.ReplayJob`, `IJobStore.Replay`, `PostgresStore.Replay`; tests `ReplayDeadLetterJobTest` |
| **SPEC-005** | Exponential Backoff | ✅ Implemented | `PostgresStore.Nack` SQL uses `POWER(2, attempts)` + jitter; tests `NackExponentialBackoffTest`, `NackBackoffHasJitterTest` |
| **SPEC-006** | Worker Service | ✅ Implemented | New project `DistributedJobQueue.Worker`; `WorkerLoop`, `HandlerRegistry`, env var config |
| **SPEC-007** | Worker Heartbeat Thread | ✅ Implemented | `HeartbeatService.cs`; starts per-job, stops on ack/nack; extends lease |
| **SPEC-008** | Job Attempts Audit Writes | ✅ Implemented | `Lease` INSERTs, `Ack`/`Nack` UPDATEs `job_attempts` (lines 185-189, 60-65, 105-110) |
| **SPEC-009** | Pool Isolation (Fast/Batch) | ✅ Implemented | `docker-compose.yaml` defines `fast-worker` (email,sms) + `batch-worker` (report,ach) with different configs |

**All 9 specs implemented and tested.**

---

## 5. Tests: Coverage & How to Run

### ConsoleTester (Integration Tests) — `DistributedJobQueueBrokerService/PostgresStoreTest.cs`
**17 tests** (originally 10, now 17 with SPEC-003/004 additions):

| Test | What It Verifies |
|------|------------------|
| `AckSuccessTest` | Job → lease → ack → succeeded, not re-leasable |
| `AckWrongWorkerTest` | Ack from non-owner throws (ownership guard) |
| `NackRetryTest` | Nack with retries left → queued, not immediately leasable (run_after) |
| `NackExhaustsRetriesTest` | Repeated nack → dead_lettered, never reclaimed |
| `VisibiltyTestAsync` | Job leased once not leasable again while lease valid |
| `ExpiredLeaseTest` | Expired lease reclaimed, attempts incremented |
| `OrphanExhaustsRetriesTest` | Orphaned job reclaimed past max_retries → dead_lettered |
| `ConcurrentLeaseNoDuplicatesTest` | 20 concurrent leases vs 5 jobs → exactly 5 distinct leases |
| `IdempotencyKeyDedupeTest` | Same idempotency_key → same job_id |
| `HeartbeatExtendsLeaseTest` | Heartbeat extends lease, prevents reclaim |
| `GetJobReturnsExistingJobTest` | GET /jobs/{id} returns all fields |
| `GetJobReturnsNullForMissingTest` | GET missing → null |
| `ListJobsFiltersByStatusTest` | ListJobs with status filter |
| `ListJobsFiltersByTypeTest` | ListJobs with type filter |
| `ReplayDeadLetterJobTest` | Replay resets dead_lettered → queued, attempts=0 |
| `ReplayNonDeadLetterJobFailsTest` | Replay on non-DLQ throws |
| `NackExponentialBackoffTest` | Backoff grows exponentially |
| `NackBackoffHasJitterTest` | Two jobs same attempt → different run_after |

**Run:**
```bash
# 1. Ensure Postgres is running
docker ps  # should show dist_job_queue

# 2. Reset DB
./scripts/initdbfortests.sh

# 3. Run tests
cd DistributedJobQueueBrokerService
dotnet run --project DistributedJobQueue.ConsoleTester.csproj
# Press Ctrl+C after tests complete (host runs indefinitely)
```

### Worker Tests — `DistributedJobQueue.Worker.Tests/Program.cs`
**7 unit/integration tests** covering `HandlerRegistry`, handlers, and end-to-end `WorkerLoop` flow.

**Run:**
```bash
cd DistributedJobQueue.Worker.Tests
dotnet run --project DistributedJobQueue.Worker.Tests.csproj
```

### Test Gaps / Not Covered
- **No API controller tests** — `JobsController` has no dedicated test project
- **No load/concurrency tests** beyond `ConcurrentLeaseNoDuplicatesTest`
- **No docker-compose integration test** (full stack: API + workers + Postgres)
- **No failure injection tests** (network partitions, DB restarts, etc.)
- **No handler sandboxing test** (handlers run in-process; no isolation)

---

## 6. Loose Ends, TODOs & Risks

### ⚠️ High Risk / Known Issues

| # | Issue | Location | Why It Matters |
|---|-------|----------|----------------|
| 1 | **No `finished_at` set on job success** | `PostgresStore.Ack` (line 40-69) updates `status` and `result_url` but **never sets `finished_at`** | Job appears "incomplete" in queries; DLQ inspection can't tell when it actually finished |
| 2 | **Lease query double-counts attempts on reclaim** | `PostgresStore.Lease` line 149: `attempts = CASE WHEN status = 'Running' THEN attempts + 1 ELSE attempts END` | If a job was `Running` (expired lease), it increments attempts AGAIN on reclaim. Combined with Nack also incrementing, attempts can skip numbers or exceed max_retries prematurely. |
| 3 | **No index on `job_attempts(job_id)`** | `JobQueueDbInit.sql` | Audit queries by job_id will be slow as table grows |
| 4 | **`RunAfter` handling in Save** | `PostgresStore.Save` line 307: `now() + @RunAfter` | If `RunAfter` is null, this becomes `now() + NULL` = NULL in Postgres — works but implicit. Should be explicit `CASE WHEN @RunAfter IS NOT NULL THEN now() + @RunAfter ELSE NULL END` |
| 5 | **No graceful shutdown in WorkerLoop** | `WorkerLoop.StopAsync` (line 102-106) | Just logs; doesn't wait for in-flight jobs or cancel heartbeat tasks |
| 6 | **Heartbeat extension calculation** | `HeartbeatService.cs:38` | Extends by `interval + 5s` — if interval=10s, extends by 15s. If lease=20s, heartbeat at 10s extends to 25s. Works but tight coupling between config values. |

### 🟡 Medium Risk / Enhancements

| # | Issue | Location |
|---|-------|----------|
| 7 | **No metrics / observability** | — | No Prometheus, no health endpoints, no structured logging |
| 8 | **Single Postgres = SPOF** | `docker-compose.yaml` | Architecture doc explicitly calls this out as v2 concern |
| 9 | **Handler registry is in-memory, no hot-reload** | `HandlerRegistry.cs` | Adding new job types requires worker restart |
| 10 | **No dead-letter alerting / UI** | — | DLQ jobs just sit there; ops has to manually query |
| 11 | **`Priority` column unused in API** | `JobsController.Save` | Accepts priority but no endpoint to update it |
| 12 | **No request validation on API** | `JobsController` | No FluentValidation / DataAnnotations; relies on Dapper/Postgres to fail |

### 🟢 Low Risk / Nice-to-Have

| # | Idea |
|---|------|
| 13 | Add `GET /jobs/{id}/attempts` endpoint for audit trail |
| 14 | Add `POST /jobs/bulk` for batch enqueue |
| 15 | Implement job cancellation (set status to cancelled, stop lease) |
| 16 | Add scheduled jobs (cron-like `run_after` with recurrence) |

---

## 7. Suggested Reading Order (5–8 Files)

Read in this order to rebuild mental model fastest:

1. **`BrokerService/Persistence/PostgresStore.cs`** — The heart: all SQL, leasing, ack/nack/heartbeat/replay, audit writes
2. **`BrokerService/Domain/Job.cs` + `JobStatus.cs` + `LeasedJob.cs`** — Domain models (5 min)
3. **`DistributedJobQueue.Worker/WorkerLoop.cs`** — Worker lease/run/ack loop, concurrency control
4. **`DistributedJobQueue.Worker/HeartbeatService.cs`** — Per-job heartbeat thread lifecycle
5. **`DistributedJobQueue.Api/Controllers/JobsController.cs`** — All REST endpoints, request DTOs
6. **`db/JobQueueDbInit.sql`** — Schema, indexes, `job_attempts` table
7. **`DistributedJobQueueBrokerService/PostgresStoreTest.cs`** — 17 integration tests = executable spec
8. **`job-queue-v1-architecture.md`** — Design rationale, state machine, guarantees table

---

## 8. Most Sensible Next Spec: SPEC-010 — Observability & Graceful Shutdown

### Why This One?
- **Addresses 3 high-risk items at once:** #5 (graceful shutdown), #7 (metrics), #10 (DLQ visibility)
- **Unblocks production readiness** — current code works for dev/test but isn't operable
- **Natural progression** — all core queue mechanics are done; observability is the next layer

### Suggested Scope
| Component | Change |
|-----------|--------|
| `WorkerLoop` | `StopAsync`: wait for `_concurrencyLimiter` (drain in-flight), cancel heartbeats, log drain status |
| `HeartbeatService` | Accept `CancellationToken` from `WorkerLoop` for coordinated shutdown |
| `PostgresStore` / `JobsController` | Add `GET /jobs/{id}/attempts` → returns `job_attempts` rows |
| `DistributedJobQueue.Api` | Add `/health` + `/metrics` (Prometheus) endpoints |
| `docker-compose.yaml` | Add `prometheus` + `grafana` services (optional, v2) |
| Tests | Add `WorkerLoopGracefulShutdownTest` (enqueue N slow jobs, stop worker, verify all acked or re-queued) |

### Alternative: SPEC-010 — Dead Letter Operations UI
If you prefer a user-facing feature: add `GET /jobs?status=dead_lettered&include_attempts=true` and a simple admin page. But observability/shutdown is more foundational.

---

## Quick Commands Reference

```bash
# Start full stack
docker compose up -d

# Run ConsoleTester (integration tests)
./scripts/initdbfortests.sh
cd DistributedJobQueueBrokerService && dotnet run --project DistributedJobQueue.ConsoleTester.csproj

# Run Worker tests
cd DistributedJobQueue.Worker.Tests && dotnet run --project DistributedJobQueue.Worker.Tests.csproj

# Build everything
dotnet build DistributedJobQueueBrokerService/DistributedJobQueueBrokerService.sln

# View logs
docker compose logs -f fast-worker
docker compose logs -f batch-worker
docker compose logs -f postgres
```