# Distributed Job Queue — v1 Architecture & Specification

## 1. Scope and Model Decisions (locked)

| Decision | Choice | Rationale |
|---|---|---|
| Execution model | Handler-registry (pre-deployed code, routed by `type`) | Avoids building a sandboxed arbitrary-code executor (Lambda-scale problem) |
| Deployment | Local (Docker Compose) | Focus on coordination logic, not infra provisioning |
| Job metadata | Persistent (Postgres) | Source of truth must survive crashes/restarts |
| Job execution | Stateless workers | Enables horizontal scaling with no coordination between workers |
| Delivery guarantee | At-least-once | Standard for durable queues; exactly-once *effect* is pushed to handlers via idempotency |
| Broker internal structure | Single unified service (API + leasing) | Splitting into two services would share the same Postgres dependency — adds complexity, not resilience |
| Scaling | Manual (`docker compose --scale`) in v1 | Feedback-driven autoscaling is a v2 concern (needs metrics + HPA) |

---

## 2. High-Level Architecture

```
                         ┌─────────────────────────┐
  Submitters ──POST────▶ │   Broker Service         │
  (services/scripts)     │   - REST API             │
                         │   - Leasing logic         │◀────────────┐
                         │   - Retry/backoff logic   │              │
                         └───────────┬─────────────┘              │
                                     │                              │
                              Postgres (jobs, attempts)             │
                                     │                              │
                    ┌────────────────┴────────────────┐            │
                    ▼                                  ▼            │
         ┌─────────────────────┐          ┌─────────────────────┐  │
         │  Worker Pool: FAST   │          │ Worker Pool: BATCH  │  │
         │  (N replicas)        │          │ (M replicas)        │──┘
         │  types: email, sms   │          │ types: report, ach  │
         │  ThreadPool(size=20) │          │ ThreadPool(size=4)  │
         └─────────┬───────────┘          └─────────┬───────────┘
                   │                                  │
                   ▼                                  ▼
          Notification System                 Local Volume (blob storage stand-in)
                                               result_url stored back in jobs table
```

---

## 3. Broker Service — API Spec

### `POST /jobs`
Enqueue a new job.

**Request:**
```json
{
  "type": "generate_ach_file",
  "payload": { "batch_id": "b-123", "accounts": [...] },
  "priority": 5,
  "max_retries": 3,
  "idempotency_key": "ach-b-123",
  "run_after": null
}
```
**Response:** `202 Accepted`
```json
{ "job_id": "uuid", "status": "queued" }
```
Notes:
- If `idempotency_key` already exists on a non-failed job, return the existing `job_id` instead of creating a duplicate (dedupes retried submissions from the caller side, distinct from handler-level idempotency).
- Never blocks on execution.

### `POST /jobs/lease`
Worker requests the next available job.

**Request:**
```json
{ "worker_id": "worker-fast-3", "allowed_types": ["send_card_expiry_reminder"], "lease_seconds": 20 }
```
**Response:** `200 OK` (job) or `204 No Content` (nothing available)
```json
{ "job_id": "uuid", "type": "...", "payload": {...}, "attempt": 1 }
```
**Implementation (Postgres):**
```sql
UPDATE jobs
SET status = 'running',
    leased_by = $worker_id,
    lease_expires_at = now() + interval '$lease_seconds seconds'
WHERE id = (
  SELECT id FROM jobs
  WHERE status = 'queued'
    AND type = ANY($allowed_types)
    AND (run_after IS NULL OR run_after <= now())
  ORDER BY priority DESC, created_at ASC
  FOR UPDATE SKIP LOCKED
  LIMIT 1
)
RETURNING *;
```
`SKIP LOCKED` is what prevents two workers from leasing the same row concurrently — the core correctness primitive of this whole system.

### `POST /jobs/{id}/heartbeat`
Worker extends its lease while still executing (prevents a live job from being re-leased to another worker).
```json
{ "worker_id": "worker-fast-3", "lease_seconds": 20 }
```
Fails (409) if `leased_by` doesn't match caller — protects against a worker that thinks it still owns a job it actually lost (e.g. after a long GC pause).

### `POST /jobs/{id}/ack`
Mark success.
```json
{ "worker_id": "worker-fast-3", "result_url": "file:///data/reports/job1.pdf" }
```
→ `status = succeeded`

### `POST /jobs/{id}/nack`
Report failure. **Broker owns the retry decision**, not the worker.
```json
{ "worker_id": "worker-fast-3", "error": "upstream timeout" }
```
Broker logic:
```
attempts += 1
if attempts >= max_retries:
    status = 'dead_lettered'
else:
    status = 'queued'
    run_after = now() + backoff(attempts)   # e.g. exponential + jitter
```

### `GET /jobs/{id}`
Status + history lookup for polling clients.

### `GET /jobs?status=dead_lettered&type=...`
DLQ browsing for the ops view.

### `POST /jobs/{id}/replay`
Reset a dead-lettered job back to `queued`, `attempts = 0`. Manual recovery path.

---

## 4. Data Model

**`jobs` table** (current state — one row per job)
```sql
CREATE TABLE jobs (
  id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  type              TEXT NOT NULL,
  payload           JSONB NOT NULL,
  status            TEXT NOT NULL DEFAULT 'queued',
    -- queued | running | succeeded | failed_retryable | dead_lettered
  priority          INT NOT NULL DEFAULT 0,
  attempts          INT NOT NULL DEFAULT 0,
  max_retries       INT NOT NULL DEFAULT 3,
  idempotency_key   TEXT UNIQUE,
  leased_by         TEXT,
  lease_expires_at  TIMESTAMPTZ,
  run_after         TIMESTAMPTZ,
  result_url        TEXT,
  last_error        TEXT,
  created_at        TIMESTAMPTZ NOT NULL DEFAULT now(),
  finished_at       TIMESTAMPTZ
);
CREATE INDEX idx_jobs_lease ON jobs (status, type, priority, created_at);
```

**`job_attempts` table** (append-only history — for debugging + DLQ inspection)
```sql
CREATE TABLE job_attempts (
  id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  job_id       UUID REFERENCES jobs(id),
  attempt_num  INT NOT NULL,
  worker_id    TEXT NOT NULL,
  outcome      TEXT NOT NULL, -- succeeded | failed
  error        TEXT,
  started_at   TIMESTAMPTZ NOT NULL,
  finished_at  TIMESTAMPTZ
);
```
Splitting current-state (`jobs`) from history (`job_attempts`) keeps the hot leasing query on a small, indexable table while preserving full audit trail for DLQ review — a pattern worth calling out explicitly in a design writeup.

**State machine:**
```
queued ──lease──▶ running ──ack──▶ succeeded
                     │
                    nack
                     │
          ┌──────────┴──────────┐
          ▼                     ▼
   attempts < max        attempts >= max
   queued (run_after     dead_lettered
   = now + backoff)           │
                            replay
                               │
                               ▼
                            queued (attempts=0)
```
Also: `running → queued` automatically if `lease_expires_at < now()` and no heartbeat/ack/nack arrived (a background sweeper job or a check inside the lease query itself treats expired leases as available again).

---

## 5. Worker Service — Behavior Spec

```python
HANDLER_REGISTRY = {
    "generate_payment_report": handle_payment_report,
    "send_card_expiry_reminder": handle_expiry_reminder,
    "generate_ach_file": handle_ach_file,
}

def worker_loop(allowed_types, pool_size):
    executor = ThreadPoolExecutor(max_workers=pool_size)
    while True:
        job = broker.lease(allowed_types)
        if job:
            executor.submit(run_job, job)
        else:
            sleep(poll_interval)

def run_job(job):
    stop_heartbeat = start_heartbeat_thread(job.id, interval=10)
    try:
        handler = HANDLER_REGISTRY[job.type]
        result = handler(job.payload)
        broker.ack(job.id, result_url=result)
    except Exception as e:
        broker.nack(job.id, error=str(e))
    finally:
        stop_heartbeat()
```

Configuration per worker pool (env vars):
```
ALLOWED_TYPES=send_card_expiry_reminder
POOL_SIZE=20
LEASE_SECONDS=20
HEARTBEAT_INTERVAL=10   # must be well under LEASE_SECONDS
```

**Fast pool** (`send_card_expiry_reminder`): high replica count, small thread pool per instance, short lease.
**Batch pool** (`generate_payment_report`, `generate_ach_file`): fewer replicas, small pool size (CPU-bound), longer lease + active heartbeating.

---

## 6. At-Least-Once Delivery — What's Guaranteed vs What's Not

| Guarantee | Owner | Mechanism |
|---|---|---|
| Job is never silently lost | Broker | Durable Postgres row; lease expiry returns orphaned jobs to `queued` |
| Job may be delivered more than once | Broker (by design) | Worker crash after `handler()` succeeds but before `ack()` reaches the broker → lease expires → another worker re-runs it |
| Job is never partially "half-applied" without a trace | Handler | Handler must be idempotent (check-before-act, or use `idempotency_key` to dedupe side effects with downstream systems) |
| Duplicate submissions from a caller are deduped | Broker | Unique constraint on `idempotency_key` at `POST /jobs` |

This is the standard split in at-least-once systems (SQS, Celery, Kafka consumers all work this way): **the broker guarantees the job won't be lost; the handler guarantees the job's effect won't be duplicated.** Worth stating explicitly in any interview/writeup — conflating these two is a common mistake.

---

## 7. Job Output Handling

- `handle_payment_report` writes the PDF to a shared Docker volume mounted at `/data/outputs`, then returns the path.
- Broker stores this in `result_url`.
- v1: local volume. v2 stretch: swap to S3-compatible storage (MinIO locally, or real S3) — this should be the *only* thing that changes; handler interface (`return output_location`) stays identical, proving the storage abstraction is clean.

---

## 8. Explicitly Out of Scope for v1 (and why)

- **Dynamic/arbitrary code execution** — would require sandboxing (Firecracker/gVisor-class problem); handler-registry avoids this deliberately.
- **Automatic scaling** — needs a metrics endpoint + HPA/controller; v1 proves correctness first, v2 adds the feedback loop.
- **Broker/datastore high availability** — single Postgres instance is a single point of failure in v1. Real resilience here would mean Postgres replication + failover, which is a legitimate v2/v3 milestone but a different (harder) problem than worker coordination.
- **Exactly-once delivery** — not attempted; at-least-once + idempotent handlers is the standard, provable approach.

---

## 9. v1 Milestone Checklist

1. Postgres schema + `SKIP LOCKED` lease query, tested with concurrent workers manually.
2. Broker API: enqueue, lease, heartbeat, ack, nack, get, list.
3. Worker: thread pool loop + 3 handlers (fast, slow, flaky-simulated).
4. Retry + backoff + DLQ transition, verified by forcing a handler to fail N times.
5. Replay endpoint, verified by DLQ'ing a job then replaying it to success.
6. Docker Compose: broker + Postgres + fast pool (replicas=3) + batch pool (replicas=2).
7. Load test: fire 100 jobs across all types simultaneously, observe pool isolation (fast jobs don't queue behind batch jobs).
