# SPEC-UI-CLIENT: Implementation Plan (Updated Architecture)

## Architecture Decision: Worker as Standalone HTTP Client

**Key change**: Worker service (`DistributedJobQueue.Worker`) should be **standalone** - no direct DB access. It calls the **Main API** (`DistributedJobQueue.Api`) via HTTP for all queue operations:
- `POST /api/jobs/lease` - Get next job
- `POST /api/jobs/ack` - Acknowledge completion
- `POST /api/jobs/nack` - Negative acknowledge (retry/fail)
- `POST /api/jobs/heartbeat` - Extend lease

**Admin endpoints** stay in Worker service (need in-flight job state):
- `POST /admin/jobs/{id}/abort` - Force-abort running job
- `POST /admin/jobs/{id}/simulate-crash` - Simulate worker crash
- `POST /admin/shutdown` - Graceful shutdown
- `POST /admin/stop-heartbeat` - Stop all heartbeats
- `GET /admin/workers` - Worker info

---

## Phase 0: Refactor Worker to HTTP Client (Backend)

| Ticket | Title | Description | Estimate |
|--------|-------|-------------|----------|
| **WKR-001** | Remove Core reference from Worker | Remove `ProjectReference` to `DistributedJobQueue.Core` from Worker csproj. Remove `PostgresStore` usage. | 0.5 day |
| **WKR-002** | Create API client in Worker | Add `HttpClient` + typed client for Main API calls (lease, ack, nack, heartbeat). Configure base URL from config. | 0.5 day |
| **WKR-003** | Refactor WorkerLoop to use HTTP | Replace `IJobStore` calls with HTTP calls to Main API. Handle retries, timeouts, auth (if added later). | 1 day |
| **WKR-004** | Add AdminController to Worker | Keep admin endpoints in Worker: abort, simulate-crash, shutdown, stop-heartbeat, workers. Uses in-flight job tracking. | 0.5 day |
| **WKR-005** | Worker HTTP server setup | Configure Kestrel on port 5001, Swagger, health checks, metrics. Keep WorkerLoop as hosted service. | 0.5 day |
| **WKR-006** | Update docker-compose | Worker service env: `API_BASE_URL=http://api:8080` (or localhost:5000). Remove DB connection string from worker. | 0.5 day |

**Total Phase 0: ~3.5 days**

---

## Phase 1: Main API Enhancements (for Worker HTTP client)

| Ticket | Title | Description | Estimate |
|--------|-------|-------------|----------|
| **API-001** | Ensure all endpoints support Worker | Verify `/api/jobs/lease`, `/ack`, `/nack`, `/heartbeat` work for Worker HTTP client. Add CORS if needed. | 0.5 day |
| **API-002** | Add Abort endpoint to Main API | `POST /api/jobs/{id}/abort` - Main API marks job DeadLettered (for UI force-abort). | 0.5 day |
| **API-003** | Add SimulateCrash endpoint to Main API | `POST /api/jobs/{id}/simulate-crash` - Main API clears lease (for UI crash simulation). | 0.5 day |
| **API-004** | Workers List endpoint in Main API | `GET /api/workers` - Query distinct workers from jobs table + last heartbeat. | 0.5 day |

**Total Phase 1: ~2 days**

---

## Phase 2: UI Project Setup & Foundation

| Ticket | Title | Description | Estimate |
|--------|-------|-------------|----------|
| **UI-001** | Initialize React + Vite + TS | `npm create vite@latest DistributedJobQueue.UI -- --template react-ts`, install deps | 0.5 day |
| **UI-002** | Configure Tailwind + shadcn/ui | Install Tailwind, run `npx shadcn@latest init`, add base components | 0.5 day |
| **UI-003** | Set up API clients (Main + Worker) | `mainApi.ts` (jobs, metrics), `workerApi.ts` (admin ops) with TanStack Query hooks | 1 day |
| **UI-004** | Define TypeScript types | Mirror C# DTOs: Job, JobAttempt, WorkerInfo, NewJobRequest | 0.5 day |
| **UI-005** | Adaptive polling hook | `useAdaptivePolling.ts` - 500ms/2s/5s based on job states | 0.5 day |
| **UI-006** | Layout & routing | `App.tsx` with header, sidebar, React Router for `/dashboard`, `/jobs/:id`, `/test-scenarios` | 0.5 day |

**Total Phase 2: ~3.5 days**

---

## Phase 3: Core Dashboard Components

| Ticket | Title | Description | Estimate |
|--------|-------|-------------|----------|
| **UI-007** | Status Cards | 4 cards: Queued, Running, Succeeded, DeadLettered counts | 0.5 day |
| **UI-008** | Job Table | TanStack Table with columns: ID, Type, Status, Worker, Attempt/Max, Priority, Lease Expiry, Created, Actions | 1 day |
| **UI-009** | Job Row + Actions | Dropdown: View Detail, **Abort (Worker API)**, **Simulate Crash (Worker API)**, Replay (Main API) | 0.5 day |
| **UI-010** | Job Detail Modal | Sheet with full job info, payload (JSON), result URL, error, timeline | 1 day |
| **UI-011** | Attempt History | Table of attempts with outcome, error, timing, worker | 0.5 day |
| **UI-012** | Dashboard Page | Compose StatusCards + JobTable + adaptive polling | 0.5 day |

**Total Phase 3: ~4 days**

---

## Phase 4: Worker Panel & Metrics

| Ticket | Title | Description | Estimate |
|--------|-------|-------------|----------|
| **UI-013** | Worker Panel | Cards per worker from Worker API `/admin/workers`: pool, poolSize, inFlight, lastHeartbeat, status, actions | 1 day |
| **UI-014** | Metrics Charts | Tremor charts: Throughput, Latency, Queue Depth, Retry Rate; parse Main API `/metrics` | 1.5 days |
| **UI-015** | Prometheus Parser | Parse Prometheus text format, extract gauge/counter/histogram values | 0.5 day |
| **UI-016** | Timeline/Gantt View | Horizontal bars per job from CreatedAt to FinishedAt/now, colored by status | 1 day |

**Total Phase 4: ~4 days**

---

## Phase 5: Job Dispatch & Test Scenarios

| Ticket | Title | Description | Estimate |
|--------|-------|-------------|----------|
| **UI-017** | Job Dispatch Form | Type, Payload (JSON), Count, MaxRetries, Priority, IdempotencyKey prefix, RunAfter delay | 1 day |
| **UI-018** | Test Scenario Definitions | 10 scenarios matching ConsoleTester: throughput, retry-backoff, lease-expiry, heartbeat, concurrent-lease, priority, idempotency, dlq, pool-isolation, graceful-shutdown | 1.5 days |
| **UI-019** | Test Scenario Panel | Grid of scenario cards with Run button, status, results | 1 day |
| **UI-020** | Test Scenarios Page | Compose JobDispatchForm + TestScenarioPanel | 0.5 day |

**Total Phase 5: ~4 days**

---

## Phase 6: Polish & Integration

| Ticket | Title | Description | Estimate |
|--------|-------|-------------|----------|
| **UI-021** | Error handling & toasts | Global error boundary, toast notifications | 0.5 day |
| **UI-022** | Loading & empty states | Skeleton loaders, empty states, polling indicator | 0.5 day |
| **UI-023** | Responsive tweaks | Mobile-friendly table, collapsible sidebar, chart resize | 0.5 day |
| **UI-024** | Docker Compose integration | Add `worker` (port 5001), `ui` (nginx) services; proxy API (5000) and Worker API (5001) | 1 day |
| **UI-025** | Documentation & README | UI setup, env vars, dev/prod commands | 0.5 day |

**Total Phase 6: ~3 days**

---

## Summary

| Phase | Tickets | Estimated Days |
|-------|---------|----------------|
| Phase 0: Worker as HTTP Client | 6 | 3.5 |
| Phase 1: Main API Enhancements | 4 | 2.0 |
| Phase 2: UI Project Setup | 6 | 3.5 |
| Phase 3: Core Dashboard | 6 | 4.0 |
| Phase 4: Worker Panel & Metrics | 4 | 4.0 |
| Phase 5: Dispatch & Test Scenarios | 4 | 4.0 |
| Phase 6: Polish & Integration | 5 | 3.0 |
| **Total** | **35** | **~24 days** |

---

## API Surface Summary

### Main API (`DistributedJobQueue.Api` - port 5000)
| Endpoint | Method | Called By |
|----------|--------|-----------|
| `/api/jobs/save` | POST | UI (JobDispatchForm) |
| `/api/jobs/lease` | POST | **Worker (HTTP)** |
| `/api/jobs/ack` | POST | **Worker (HTTP)** |
| `/api/jobs/nack` | POST | **Worker (HTTP)** |
| `/api/jobs/heartbeat` | POST | **Worker (HTTP)** |
| `/api/jobs/{id}` | GET | UI |
| `/api/jobs` | GET | UI |
| `/api/jobs/{id}/replay` | POST | UI |
| `/api/jobs/{id}/attempts` | GET | UI |
| `/api/jobs/{id}/abort` | POST | UI (force-abort) |
| `/api/jobs/{id}/simulate-crash` | POST | UI (crash sim) |
| `/api/workers` | GET | UI (WorkerPanel) |
| `/metrics` | GET | UI (MetricsCharts) |
| `/health/*` | GET | Monitoring |

### Worker API (`DistributedJobQueue.Worker` - port 5001)
| Endpoint | Method | Called By |
|----------|--------|-----------|
| `/admin/jobs/{id}/abort` | POST | UI (immediate abort via worker's in-flight state) |
| `/admin/jobs/{id}/simulate-crash` | POST | UI (crash sim via worker's in-flight state) |
| `/admin/shutdown` | POST | UI (graceful shutdown) |
| `/admin/stop-heartbeat` | POST | UI (stop all heartbeats) |
| `/admin/workers` | GET | UI (WorkerPanel - this worker's info) |
| `/health` | GET | Monitoring |
| `/metrics` | GET | Monitoring |

---

## Dependencies Graph

```
WKR-001 ──► WKR-002 ──► WKR-003 ──► WKR-004 ──► WKR-005 ──► WKR-006
    │
    └──► API-001 ──► API-002 ──►
              API-003 ──►
              API-004 ──►

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