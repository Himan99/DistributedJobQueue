# SPEC: TypeScript UI Client for DistributedJobQueue

## Problem Statement

Developers and operators need a visual interface to interact with the DistributedJobQueue system. Currently, all interactions happen via raw API calls or the ConsoleTester integration tests. There's no way to:
- Visually monitor job status in real-time
- Dispatch test job batches for specific scenarios
- Kill/abort jobs to test failure handling
- View worker pool status and in-flight jobs
- Visualize metrics (throughput, latency, retry rates)
- Inspect job attempt history for debugging

This creates friction during development, testing, and operational debugging.

## Solution

Build a React + TypeScript single-page application (DistributedJobQueue.UI) that provides a live dashboard for the DistributedJobQueue system. The UI will:

1. **Dashboard** - Real-time job table with status, worker assignment, lease expiry, attempt count
2. **Job Management** - Create jobs, dispatch test batches, force-abort running jobs, simulate worker crashes
3. **Worker Panel** - View active workers, pool sizes, in-flight counts, last heartbeat
4. **Metrics Visualization** - Throughput, latency, queue depth, retry rate charts using Tremor
5. **Job Detail View** - Attempt history timeline, payload inspection, error details
6. **Test Scenarios** - Pre-configured test suites for all 10 guarantee scenarios

The UI communicates with the existing ASP.NET Core API via REST, using adaptive polling for real-time updates. New API endpoints will be added for force-abort, crash simulation, and worker admin operations.

## User Stories

### Dashboard & Monitoring
1. As a **developer**, I want to see a live table of all jobs with their status, type, worker, attempt, and lease expiry, so that I can monitor queue health at a glance.
2. As a **developer**, I want status summary cards showing counts of Queued, Running, Succeeded, and DeadLettered jobs, so that I can quickly assess system state.
3. As a **developer**, I want adaptive polling (500ms when jobs running, 2s when only queued, 5s when idle), so that the UI stays responsive during active tests without hammering the API when idle.
4. As a **developer**, I want to filter and sort jobs by status, type, worker, and time range, so that I can focus on relevant jobs during debugging.

### Job Dispatch & Test Scenarios
5. As a **QA engineer**, I want to dispatch batches of N jobs of a specific type with configurable payload, priority, and idempotency keys, so that I can test throughput and priority ordering.
6. As a **QA engineer**, I want pre-built test scenario buttons for: basic throughput, retry/backoff behavior, lease expiration, heartbeat extension, concurrent lease safety, priority ordering, idempotency deduplication, dead letter queue replay, pool isolation, and graceful shutdown, so that I can verify all system guarantees with one click.
7. As a **developer**, I want to create a single job with custom payload/type/priority/idempotency key, so that I can test edge cases manually.

### Job Control (Kill Actions)
8. As a **developer**, I want to **force-abort a running job** (mark dead_lettered immediately, bypassing retry logic), so that I can test DLQ handling and cleanup.
9. As a **developer**, I want to **simulate a worker crash** on a specific job (clear lease/heartbeat, let natural re-lease occur), so that I can verify lease expiration and re-processing guarantees.
10. As an **operator**, I want to **trigger graceful shutdown of a worker process** via the Worker Admin API, so that I can test graceful shutdown behavior without Docker CLI access.
11. As a **developer**, I want to **send cancellation to an in-flight handler** (cooperative cancellation), so that I can test handler cooperation with shutdown signals.

### Worker Visibility
12. As an **operator**, I want to see a panel of all known workers with their pool (fast/batch), pool size, in-flight count, last heartbeat, and status, so that I can verify worker health and pool isolation.
13. As a **developer**, I want to see which job types each worker pool processes, so that I can verify pool isolation (email/sms → fast, report/ach → batch).

### Job Detail & History
14. As a **developer**, I want to click a job and see its full attempt history with outcome, error, timing, and worker, so that I can debug failures.
15. As a **developer**, I want to see the job payload and result URL, so that I can verify correct data flow.
16. As a **developer**, I want a timeline/Gantt view of job lifecycle (queued → running → succeeded/failed), so that I can visualize timing and overlaps.

### Metrics & Analytics
17. As a **developer**, I want charts for throughput (jobs/sec), latency (lease→ack), retry rate, and queue depth over time, so that I can analyze performance trends.
18. As a **developer**, I want metrics to pull from Prometheus /metrics endpoint, so that they match what operators see in Grafana.

### Technical Requirements
19. As a **developer**, I want the UI built with React + Vite + TypeScript, so that I get fast HMR, type safety, and modern tooling.
20. As a **developer**, I want TanStack Query for server state management, so that caching, background refetching, and optimistic updates work out of the box.
21. As a **developer**, I want shadcn/ui + Tailwind CSS for components, so that I get accessible, customizable components without version lock-in.
22. As a **developer**, I want Tremor for charts, so that I get dashboard-ready, Tailwind-native chart components with minimal code.
23. As a **developer**, I want the UI project at `DistributedJobQueue.UI/` in the repo root, so that it has its own package.json, build, and deploy pipeline.
24. As a **developer**, I want Vite proxy in dev mode (UI at localhost:5173 → API at localhost:5xxx), so that CORS isn't an issue during development.
25. As an **operator**, I want Docker Compose integration for production deployment, so that UI and API deploy together.

## Implementation Decisions

### New API Endpoints Required

The following endpoints must be added to the ASP.NET Core API (`DistributedJobQueue.Api`):

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/jobs/{id}/abort` | POST | Force-abort running job → mark DeadLettered immediately, stop heartbeat, release lease |
| `/api/jobs/{id}/simulate-crash` | POST | Clear lease/heartbeat for job → let natural lease expiration trigger re-lease to another worker |
| `/api/workers` | GET | List all known workers with pool, pool size, in-flight count, last heartbeat |
| `/api/workers/{workerId}/shutdown` | POST | Trigger graceful shutdown of specific worker (Admin API) |
| `/api/workers/{workerId}/stop-heartbeat` | POST | Stop heartbeat for specific worker's jobs (simulate crash) |

**Worker Admin API**: Add a minimal admin controller to the Worker service (`DistributedJobQueue.Worker`) exposing:
- `POST /admin/shutdown` - Initiate graceful shutdown (calls `StopAsync`)
- `POST /admin/stop-heartbeat` - Stop heartbeat service for all in-flight jobs

### TypeScript Types (Mirror C# DTOs)

```typescript
// Job status enum matching C# JobStatus
type JobStatus = 'Queued' | 'Running' | 'Succeeded' | 'DeadLettered';

interface Job {
  jobId: string;
  type: string;
  payload: string;
  status: JobStatus;
  attempts: number;
  maxRetries: number;
  priority: number;
  idempotencyKey?: string;
  leasedBy?: string;
  leaseExpiresAt?: string; // ISO 8601
  runAfter?: string;
  resultUrl?: string;
  lastError?: string;
  createdAt: string;
  finishedAt?: string;
}

interface JobAttempt {
  id: string;
  jobId: string;
  attemptNum: number;
  workerId: string;
  outcome: 'Acked' | 'Nacked' | 'Failed' | 'TimedOut';
  error?: string;
  startedAt: string;
  finishedAt?: string;
}

interface WorkerInfo {
  workerId: string;
  pool: 'fast' | 'batch';
  allowedTypes: string[];
  poolSize: number;
  inFlightCount: number;
  lastHeartbeat?: string;
  status: 'healthy' | 'stopping' | 'stopped';
}

interface NewJobRequest {
  type: string;
  payload: string;
  maxRetries?: number;
  idempotencyKey?: string;
  runAfter?: string; // ISO 8601 duration
  priority?: number;
}
```

### API Client Layer (TanStack Query)

```typescript
// api/client.ts - Axios instance with base URL from Vite env
// api/jobs.ts - useJobs, useJob, useCreateJob, useAbortJob, useSimulateCrash, useReplayJob, useJobAttempts
// api/workers.ts - useWorkers, useWorkerShutdown, useWorkerStopHeartbeat
// api/metrics.ts - useMetrics (polls /metrics, parses Prometheus text format)
```

### Adaptive Polling Strategy

```typescript
// hooks/useAdaptivePolling.ts
// Returns refetchInterval based on job states:
// - Any job Running → 500ms
// - Any job Queued (none Running) → 2000ms
// - No jobs Queued/Running → 5000ms
```

### Project Structure

```
DistributedJobQueue.UI/
├── package.json
├── vite.config.ts
├── tsconfig.json
├── tsconfig.node.json
├── tailwind.config.js
├── postcss.config.js
├── index.html
├── .env.example
├── src/
│   ├── main.tsx
│   ├── App.tsx
│   ├── vite-env.d.ts
│   ├── api/
│   │   ├── client.ts
│   │   ├── jobs.ts
│   │   ├── workers.ts
│   │   └── metrics.ts
│   ├── components/
│   │   ├── JobTable.tsx
│   │   ├── JobRow.tsx
│   │   ├── JobDetailModal.tsx
│   │   ├── StatusCards.tsx
│   │   ├── WorkerPanel.tsx
│   │   ├── AttemptHistory.tsx
│   │   ├── MetricsCharts.tsx
│   │   ├── TestScenarioPanel.tsx
│   │   ├── JobDispatchForm.tsx
│   │   ├── TimelineView.tsx
│   │   └── ui/           # shadcn/ui components (button, table, card, dialog, etc.)
│   ├── hooks/
│   │   ├── useJobs.ts
│   │   ├── useWorkers.ts
│   │   ├── useMetrics.ts
│   │   └── useAdaptivePolling.ts
│   ├── types/
│   │   ├── job.ts
│   │   ├── worker.ts
│   │   └── metrics.ts
│   ├── utils/
│   │   ├── format.ts
│   │   └── prometheus.ts
│   └── pages/
│       ├── Dashboard.tsx
│       ├── JobDetail.tsx
│       └── TestScenarios.tsx
```

### UI Component Library: shadcn/ui

Install via `npx shadcn@latest add button table card dialog input select label tabs tooltip badge progress separator scroll-area sheet`. Components are copied into `src/components/ui/`.

### Charting: Tremor

Install `@tremor/react` and use `AreaChart`, `BarChart`, `MetricCard`, `DonutChart` for dashboard metrics.

### Test Scenario Definitions

Each scenario is a function that dispatches a specific job batch and optionally polls for results:

```typescript
type TestScenario = {
  id: string;
  name: string;
  description: string;
  run: (dispatch: (jobs: NewJobRequest[]) => Promise<void>) => Promise<TestResult>;
};

const scenarios: TestScenario[] = [
  { id: 'throughput', name: 'Basic Throughput', ... },
  { id: 'retry-backoff', name: 'Retry/Backoff Behavior', ... },
  { id: 'lease-expiry', name: 'Lease Expiration', ... },
  { id: 'heartbeat', name: 'Heartbeat Extension', ... },
  { id: 'concurrent-lease', name: 'Concurrent Lease Safety', ... },
  { id: 'priority', name: 'Priority Ordering', ... },
  { id: 'idempotency', name: 'Idempotency Key Dedupe', ... },
  { id: 'dlq', name: 'Dead Letter Queue', ... },
  { id: 'pool-isolation', name: 'Pool Isolation', ... },
  { id: 'graceful-shutdown', name: 'Graceful Shutdown', ... },
];
```

## Testing Decisions

### What Makes a Good Test
- Test external behavior: UI renders correctly, API calls fire, state updates
- Mock API responses with MSW (Mock Service Worker) for unit/component tests
- Integration tests against real API (run in CI with docker-compose)
- E2E tests with Playwright for critical user flows (dispatch → monitor → abort)

### Modules to Test
| Module | Test Type | Tool |
|--------|-----------|------|
| API client hooks | Unit | Vitest + MSW |
| JobTable, WorkerPanel, MetricsCharts | Component | Vitest + React Testing Library |
| TestScenarioPanel | Integration | Vitest + MSW |
| Full dashboard flow | E2E | Playwright |

### Prior Art
- ConsoleTester (`DistributedJobQueueBrokerService/PostgresStoreTest.cs`) - 10 integration tests that define expected behaviors
- Existing API contract tests in `JobsController`
- Prometheus metrics format parsing

## Out of Scope

- **Authentication/Authorization** - No auth for v1; API remains open
- **Real-time WebSockets/SSE** - Polling only; can add later if needed
- **Multi-tenancy** - Single queue view
- **Job payload editor** - Raw JSON textarea only; no schema-aware editor
- **Historical analytics** - Only live/recent metrics (last 1 hour)
- **Worker log streaming** - Workers log to stdout; UI doesn't tail logs
- **Configuration management** - UI is read-only for config; changes via env vars
- **Mobile responsive** - Desktop-first; mobile usable but not optimized

## Further Notes

### API Changes Required Before UI Implementation
1. Add `Abort` and `SimulateCrash` methods to `IJobStore` and implement in `PostgresStore`
2. Add `Abort` and `SimulateCrash` endpoints to `JobsController`
3. Create `WorkerAdminController` in Worker service with `/admin/shutdown` and `/admin/stop-heartbeat`
4. Expose worker registry/info via new `/api/workers` endpoint in API (queries job store for distinct `LeasedBy` + last heartbeat)

### Database Considerations
- `abort` operation: UPDATE jobs SET Status=DeadLettered, FinishedAt=NOW(), LastError='Force aborted via UI' WHERE JobId=@id AND Status=Running
- `simulate-crash` operation: UPDATE jobs SET LeasedBy=NULL, LeaseExpiresAt=NULL WHERE JobId=@id AND Status=Running (lets next Lease call pick it up)

### Deployment
- Dev: `docker-compose up` for postgres + workers + API; `npm run dev` in UI folder
- Prod: Add `ui` service to docker-compose, build UI as static files served by nginx, proxy API calls

### Environment Variables
```
VITE_API_BASE_URL=http://localhost:5000  # API base URL
VITE_POLL_BASE_INTERVAL=5000             # Base polling interval (ms)
```

### Metrics Parsing
- `/metrics` returns Prometheus text format
- Parse with simple regex or `prom-client` in browser
- Key metrics: `jobs_queued_total{type}`, `jobs_running_total{type}`, `jobs_succeeded_total`, `jobs_dead_lettered_total`, `job_lease_duration_seconds_bucket`, `worker_in_flight{pool}`

### shadcn/ui Setup
```bash
npx shadcn@latest init
# Select: TypeScript, Tailwind, Slate color, CSS variables
# Then add components as needed
```

### Timeline/Gantt View
- Use simple CSS grid or Tremor's `AreaChart` with custom rendering
- Each job = horizontal bar from CreatedAt to FinishedAt (or now if Running)
- Color by status: Queued=gray, Running=blue, Succeeded=green, DeadLettered=red
- Tooltip shows job details on hover