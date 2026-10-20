# TODO / Backlog — DistributedJobQueue

*Consolidated from PROJECT_OVERVIEW.md risks, SPEC discussions, and code review*

---

## 🔴 Critical Bugs (Fix First)

### 1. Double-Increment Bug on Retry Counting
- **Location:** `BrokerService/Persistence/PostgresStore.cs:150` (Lease) + line 75 (Nack)
- **Problem:** `Lease` increments `attempts` when reclaiming an expired lease (`status = 'Running'`), AND `Nack` increments `attempts` on explicit failure. These represent the *same logical retry*.
- **Impact:** Job with `max_retries=3` dead-letters after only 2 actual handler failures if reclaimed once.
- **Reproduction:** 
  1. Job created, `attempts=0`
  2. Worker A leases, crashes → lease expires
  3. Worker B reclaims via `Lease` → `attempts` becomes 1 (bug: not a handler execution)
  4. Worker B handler fails → `Nack` → `attempts` becomes 2
  5. Worker C reclaims → `attempts` becomes 3
  6. Worker C handler fails → `Nack` → `attempts` becomes 4 → **DeadLettered** (but only 2 handler runs!)
- **Fix:** Remove `attempts + 1` in Lease line 150. Only `Nack` should increment.
- **Test needed:** Add `ExpiredLeaseReclaimDoesNotIncrementAttemptsTest` to `PostgresStoreTest.cs`

---

### 2. `finished_at` Never Set on Ack
- **Location:** `BrokerService/Persistence/PostgresStore.cs:40-69` (Ack method)
- **Problem:** `UPDATE` sets `status = 'succeeded'` and `result_url` but omits `finished_at = now()`
- **Impact:** Completed jobs show `NULL` for `finished_at`; breaks "time to complete" metrics, DLQ inspection
- **Fix:** Add `finished_at = now()` to Ack UPDATE

---

## 🟠 High Priority (Production Readiness)

### 3. Worker Graceful Shutdown
- **Location:** `DistributedJobQueue.Worker/WorkerLoop.cs:102-106` (StopAsync)
- **Problem:** Returns immediately; doesn't wait for in-flight jobs or cancel heartbeats
- **Spec:** SPEC-010 Requirement 1
- **Fix:** Drain `_concurrencyLimiter`, cancel heartbeat tokens, configurable timeout

### 4. No Health / Readiness Endpoints
- **Location:** `DistributedJobQueue.Api` — missing
- **Spec:** SPEC-010 Requirement 2

### 5. No Audit Trail API Endpoint
- **Location:** `DistributedJobQueue.Api` — `job_attempts` written but not queryable
- **Spec:** SPEC-010 Requirement 3

### 6. No Prometheus Metrics
- **Location:** `DistributedJobQueue.Api` — missing
- **Spec:** SPEC-010 Requirement 4

### 7. Missing Index on `job_attempts(job_id)`
- **Location:** `db/JobQueueDbInit.sql`
- **Spec:** SPEC-010 Requirement 5
- **Fix:** `CREATE INDEX idx_job_attempts_job_id ON job_attempts(job_id);`

---

## 🟡 Medium Priority (Enhancements)

### 8. No Graceful Shutdown Test
- **Location:** `DistributedJobQueue.Worker.Tests` — missing
- **Spec:** SPEC-010 Verification

### 9. `RunAfter` Null Handling in Save
- **Location:** `PostgresStore.Save` line 307: `now() + @RunAfter`
- **Problem:** If `RunAfter` is null, becomes `now() + NULL` = NULL (works but implicit)
- **Fix:** Explicit `CASE WHEN @RunAfter IS NOT NULL THEN now() + @RunAfter ELSE NULL END`

### 10. No Metrics / Observability
- **Location:** Entire codebase
- **Details:** No structured logging, no correlation IDs, no distributed tracing

### 11. Single Postgres = SPOF
- **Location:** `docker-compose.yaml` — one postgres instance
- **Note:** Architecture doc calls this out as v2 concern

### 12. Handler Registry No Hot-Reload
- **Location:** `HandlerRegistry.cs` — in-memory dictionary
- **Impact:** New job types require worker restart

### 13. No Dead-Letter Alerting / UI
- **Location:** — 
- **Impact:** DLQ jobs sit silently; ops must manually query

### 14. Priority Column Unused in API
- **Location:** `JobsController.Save` accepts priority but no update endpoint

### 15. No Request Validation on API
- **Location:** `JobsController` — no FluentValidation/DataAnnotations

---

## 🟢 Low Priority (Nice to Have)

### 16. `GET /jobs/{id}/attempts` Endpoint
- Part of SPEC-010 but could be standalone

### 17. `POST /jobs/bulk` for Batch Enqueue

### 18. Job Cancellation (set status to cancelled, stop lease)

### 19. Scheduled/Recurring Jobs (cron-like `run_after` with recurrence)

### 20. Distributed Tracing (OpenTelemetry)

### 21. Structured JSON Logging with Correlation IDs

### 22. Alerting Rules / Grafana Dashboards

### 23. Autoscaling Based on Queue Depth

---

## 📋 Specs Status

| Spec | Title | Status |
|------|-------|--------|
| SPEC-001 | Namespace Alignment | ✅ Done |
| SPEC-002 | Comment/Code Mismatch | ✅ Done |
| SPEC-003 | Get / List Jobs | ✅ Done |
| SPEC-004 | DLQ Replay | ✅ Done |
| SPEC-005 | Exponential Backoff | ✅ Done |
| SPEC-006 | Worker Service | ✅ Done |
| SPEC-007 | Worker Heartbeat Thread | ✅ Done |
| SPEC-008 | Job Attempts Audit Writes | ✅ Done |
| SPEC-009 | Pool Isolation (Fast/Batch) | ✅ Done |
| SPEC-010 | Observability & Graceful Shutdown | 📝 Written |
| — | Double-Increment Fix | 🐛 Bug (not spec'd) |

---

## Suggested Next Work Order

1. **Fix #1 (Double-Increment)** + **Fix #2 (finished_at)** — both in `PostgresStore.cs`, ~10 lines total
2. **SPEC-010** — Observability & Graceful Shutdown (5 requirements)
3. **Fix #7 (Index)** — trivial, unblocks audit queries
4. **Fix #9 (RunAfter null handling)** — trivial
5. Remaining SPEC-010 items + medium priority items