# SPEC-UI-CLIENT: Implementation Ticket Breakdown

Each ticket is sized for 1-2 days of work. Tickets are ordered by dependency.

---

## Architecture Decision: All New Endpoints in Worker API

**Key change from spec**: All new administrative endpoints (abort, simulate-crash, worker management) will be added to the **Worker service** (`DistributedJobQueue.Worker`) as an HTTP API, not the main API. The Worker service already has:
- Direct access to `IJobStore` and `HeartbeatService`
- In-flight job tracking (`_inFlightJobs` dictionary)
- Worker identity and pool configuration

The main API (`DistributedJobQueue.Api`) remains focused on core job queue operations. The UI will call the Worker API for admin operations.

---

## Phase 0: Worker API Prerequisites (Backend)

These must be completed before UI implementation can proceed.

| Ticket | Title | Description | Estimate |
|--------|-------|-------------|----------|
| **WKR-001** | Add HTTP server to Worker service | Convert `DistributedJobQueue.Worker` from `IHostedService` only to ASP.NET Core minimal API + hosted service. Add `Program.cs`, configure Kestrel on port 5001, keep `WorkerLoop` as hosted service. | 0.5 day |
| **WKR-002** | Add Abort endpoint in Worker API | Add `POST /admin/jobs/{id}/abort` in Worker API: mark job DeadLettered immediately, cancel heartbeat, release lease. Uses existing `IJobStore` + `HeartbeatService` + `_inFlightJobs`. | 0.5 day |
| **WKR-003** | Add Simulate Crash endpoint | Add `POST /admin/jobs/{id}/simulate-crash` in Worker API: clear lease/heartbeat for job (UPDATE jobs SET LeasedBy=NULL, LeaseExpiresAt=NULL), cancel heartbeat task, let natural re-lease occur. | 0.5 day |
| **WKR-004** | Worker Shutdown endpoint | Add `POST /admin/shutdown` - triggers graceful `StopAsync` on `WorkerLoop` (existing logic handles in-flight drain). | 0.5 day |
| **WKR-005** | Stop Heartbeat endpoint | Add `POST /admin/stop-heartbeat` - stops `HeartbeatService` for all in-flight jobs (simulates worker crash for all jobs). | 0.5 day |
| **WKR-006** | Workers List endpoint | Add `GET /admin/workers` - returns info for this worker instance: workerId, pool, allowedTypes, poolSize, inFlightCount, lastHeartbeat, status. | 0.5 day |
| **WKR-007** | Health & Metrics endpoints | Add `/health` and `/metrics` (Prometheus) to Worker API for monitoring. | 0.5 day |

**Total Phase 0: ~3.5 days**

---

## Phase 1: Project Setup & Foundation

| Ticket | Title | Description | Estimate |
|--------|-------|-------------|----------|
| **UI-001** | Initialize React + Vite + TS project | `npm create vite@latest DistributedJobQueue.UI -- --template react-ts`, install deps: `@tanstack/react-query`, `axios`, `@tremor/react`, `date-fns`, `lucide-react`, `clsx`, `tailwind-merge` | 0.5 day |
| **UI-002** | Configure Tailwind + shadcn/ui | Install Tailwind, PostCSS, Autoprefixer; run `npx shadcn@latest init`; add base components: `button`, `card`, `table`, `badge`, `input`, `select`, `label`, `dialog`, `tabs`, `tooltip`, `progress`, `separator`, `scroll-area`, `sheet` | 0.5 day |
| **UI-003** | Set up API clients (Main + Worker) | Create `src/api/mainApi.ts` (jobs: save, lease, ack, nack, heartbeat, get, list, replay, attempts) and `src/api/workerApi.ts` (abort, simulateCrash, shutdown, stopHeartbeat, workers, health) with TanStack Query hooks | 1 day |
| **UI-004** | Define TypeScript types | Create `src/types/job.ts`, `src/types/worker.ts`, `src/types/metrics.ts` mirroring C# DTOs | 0.5 day |
| **UI-005** | Adaptive polling hook | Create `src/hooks/useAdaptivePolling.ts` returning refetchInterval based on job states (500ms/2s/5s) | 0.5 day |
| **UI-006** | Layout & routing | Create `src/App.tsx` with layout (header, sidebar), React Router for `/dashboard`, `/jobs/:id`, `/test-scenarios` | 0.5 day |

**Total Phase 1: ~3.5 days**

---

## Phase 2: Core Dashboard Components

| Ticket | Title | Description | Estimate |
|--------|-------|-------------|----------|
| **UI-007** | Status Cards component | `src/components/StatusCards.tsx` - 4 cards: Queued, Running, Succeeded, DeadLettered counts with color coding | 0.5 day |
| **UI-008** | Job Table component | `src/components/JobTable.tsx` - TanStack Table (or shadcn table) with columns: ID, Type, Status, Worker, Attempt/Max, Priority, Lease Expiry, Created, Actions; sortable, filterable | 1 day |
| **UI-009** | Job Row + Actions menu | `src/components/JobRow.tsx` - Row with dropdown menu: View Detail, **Abort (if Running, calls Worker API)**, **Simulate Crash (if Running, calls Worker API)**, Replay (if DeadLettered, calls Main API) | 0.5 day |
| **UI-010** | Job Detail Modal | `src/components/JobDetailModal.tsx` - Sheet/Dialog showing full job info, payload (JSON formatted), result URL, error, timeline | 1 day |
| **UI-011** | Attempt History component | `src/components/AttemptHistory.tsx` - Table of attempts with attempt#, worker, outcome, error, started, finished, duration | 0.5 day |
| **UI-012** | Dashboard page assembly | `src/pages/Dashboard.tsx` - Compose StatusCards + JobTable + adaptive polling; wire up all actions (Main API for reads, Worker API for mutations) | 0.5 day |

**Total Phase 2: ~4 days**

---

## Phase 3: Worker Panel & Metrics

| Ticket | Title | Description | Estimate |
|--------|-------|-------------|----------|
| **UI-013** | Worker Panel component | `src/components/WorkerPanel.tsx` - Cards per worker (from Worker API `/admin/workers`): WorkerId, Pool (fast/batch badge), AllowedTypes, PoolSize, InFlight, LastHeartbeat, Status, Actions (Shutdown, Stop Heartbeat - both call Worker API) | 1 day |
| **UI-014** | Metrics Charts component | `src/components/MetricsCharts.tsx` - Tremor charts: Throughput (AreaChart), Latency (AreaChart), Queue Depth (AreaChart), Retry Rate (BarChart); parse `/metrics` from **Main API** (Prometheus format) | 1.5 days |
| **UI-015** | Prometheus parser utility | `src/utils/prometheus.ts` - Parse text format, extract gauge/counter/histogram values by label | 0.5 day |
| **UI-016** | Timeline/Gantt View | `src/components/TimelineView.tsx` - Horizontal bars per job from CreatedAt to FinishedAt/now, colored by status, tooltip on hover | 1 day |

**Total Phase 3: ~4 days**

---

## Phase 4: Job Dispatch & Test Scenarios

| Ticket | Title | Description | Estimate |
|--------|-------|-------------|----------|
| **UI-017** | Job Dispatch Form | `src/components/JobDispatchForm.tsx` - Form: Type (select), Payload (JSON textarea), Count (number), MaxRetries, Priority, IdempotencyKey prefix, RunAfter delay; "Dispatch Batch" button (calls Main API `/api/jobs/save`) | 1 day |
| **UI-018** | Test Scenario Definitions | `src/utils/testScenarios.ts` - 10 scenario functions matching ConsoleTester tests: throughput, retry-backoff, lease-expiry, heartbeat, concurrent-lease, priority, idempotency, dlq, pool-isolation, graceful-shutdown | 1.5 days |
| **UI-019** | Test Scenario Panel | `src/components/TestScenarioPanel.tsx` - Grid of scenario cards with Run button, status (pending/running/pass/fail), results summary | 1 day |
| **UI-020** | Test Scenarios Page | `src/pages/TestScenarios.tsx` - Compose JobDispatchForm + TestScenarioPanel; show live results | 0.5 day |

**Total Phase 4: ~4 days**

---

## Phase 5: Polish & Integration

| Ticket | Title | Description | Estimate |
|--------|-------|-------------|----------|
| **UI-021** | Error handling & toasts | Global error boundary, toast notifications for API errors, success confirmations | 0.5 day |
| **UI-022** | Loading & empty states | Skeleton loaders for tables, empty state illustrations, polling indicator | 0.5 day |
| **UI-023** | Responsive tweaks | Mobile-friendly table (horizontal scroll), collapsible sidebar, chart resize | 0.5 day |
| **UI-024** | Docker Compose integration | Add `worker-api` service (port 5001), `ui` service (nginx static) to docker-compose.yaml; proxy Main API (5000) and Worker API (5001) | 1 day |
| **UI-025** | Documentation & README | Update root README with UI setup instructions, environment variables, dev/prod commands | 0.5 day |

**Total Phase 5: ~3 days**

---

## Summary

| Phase | Tickets | Estimated Days |
|-------|---------|----------------|
| Phase 0: Worker API Prerequisites | 7 | 3.5 |
| Phase 1: Project Setup | 6 | 3.5 |
| Phase 2: Core Dashboard | 6 | 4.0 |
| Phase 3: Worker Panel & Metrics | 4 | 4.0 |
| Phase 4: Dispatch & Test Scenarios | 4 | 4.0 |
| Phase 5: Polish & Integration | 5 | 3.0 |
| **Total** | **32** | **~22 days** |

---

## Suggested Sprint Breakdown

**Sprint 1 (Week 1)**: Phase 0 (WKR-001 to WKR-007) — Worker API complete
**Sprint 2 (Week 2)**: Phase 1 (UI-001 to UI-006) — UI foundation complete
**Sprint 3 (Week 3)**: Phase 2 (UI-007 to UI-012) — Core dashboard working
**Sprint 4 (Week 4)**: Phase 3 (UI-013 to UI-016) — Worker visibility + metrics
**Sprint 5 (Week 5)**: Phase 4 (UI-017 to UI-020) — Test scenarios
**Sprint 6 (Week 6)**: Phase 5 (UI-021 to UI-025) — Polish + deploy

---

## API Surface Summary

### Main API (`DistributedJobQueue.Api` - port 5000)
| Endpoint | Method | Used By UI |
|----------|--------|------------|
| `/api/jobs/save` | POST | JobDispatchForm |
| `/api/jobs/lease` | POST | (Worker only) |
| `/api/jobs/ack` | POST | (Worker only) |
| `/api/jobs/nack` | POST | (Worker only) |
| `/api/jobs/heartbeat` | POST | (Worker only) |
| `/api/jobs/{id}` | GET | JobTable, JobDetail |
| `/api/jobs` | GET | JobTable (list with filters) |
| `/api/jobs/{id}/replay` | POST | JobDetail (DeadLettered only) |
| `/api/jobs/{id}/attempts` | GET | JobDetail, AttemptHistory |
| `/metrics` | GET | MetricsCharts |
| `/health/live`, `/health/ready` | GET | Monitoring |

### Worker API (`DistributedJobQueue.Worker` - port 5001)
| Endpoint | Method | Used By UI |
|----------|--------|------------|
| `/admin/jobs/{id}/abort` | POST | JobRow (Abort action) |
| `/admin/jobs/{id}/simulate-crash` | POST | JobRow (Simulate Crash action) |
| `/admin/shutdown` | POST | WorkerPanel (Shutdown action) |
| `/admin/stop-heartbeat` | POST | WorkerPanel (Stop Heartbeat action) |
| `/admin/workers` | GET | WorkerPanel |
| `/health` | GET | Monitoring |
| `/metrics` | GET | Monitoring |

---

## Dependencies Graph

```
WKR-001 ──► WKR-002 ──►
    │         WKR-003 ──► UI-003 (Worker API client needs endpoints)
    │         WKR-004 ──►
    │         WKR-005 ──►
    │         WKR-006 ──►
    └──► WKR-007

UI-001 ──► UI-002 ──► UI-003 ──► UI-004 ──► UI-005 ──► UI-006
                                                              │
                    ┌─────────────────────────────────────────┘
                    ▼
UI-007 ──► UI-008 ──► UI-009 ──► UI-010 ──► UI-011 ──► UI-012
                    │
                    └──────────────► UI-013 (Worker Panel via Worker API)
                    │
                    └──────────────► UI-014 ──► UI-015 ──► UI-016
                    │
                    └──────────────► UI-017 ──► UI-018 ──► UI-019 ──► UI-020

UI-012, UI-013, UI-016, UI-020 ──► UI-021 ──► UI-022 ──► UI-023 ──► UI-024 ──► UI-025
```