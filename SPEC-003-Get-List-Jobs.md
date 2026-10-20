# SPEC-003 — Get / List Jobs

**ID:** SPEC-003  
**Title:** Job lookup and status-filtered listing endpoints  
**Status:** Implemented  
**Type:** New API endpoints  
**Architecture ref:** Section 3 — `GET /jobs/{id}`, `GET /jobs?status=dead_lettered&type=...`

## Problem
No GET endpoints exist to look up a single job by ID or list jobs filtered by status. The broker only supports write operations (save, lease, ack, nack, heartbeat).

## Requirements
1. `GET /jobs/{id}` — returns job details: id, type, payload, status, attempts, max_retries, priority, leased_by, lease_expires_at, run_after, result_url, last_error, created_at, finished_at. Returns `404` when not found.
2. `GET /jobs?status={status}&type={type}` — returns a list of jobs, optionally filtered by `status` (queued/running/succeeded/dead_lettered) and/or `type`.
3. Both endpoints return JSON.

## Files affected
- `DistributedJobQueue.Api/Controllers/JobsController.cs` — add `GetJob(Guid id)` and `ListJobs(string? status, string? type)` endpoints
- May add a Job response DTO/record if not reusing `Job` domain model directly

## Verification
- `GET /jobs/{id}` → `200` with full job details for an existing job
- `GET /jobs/{id}` → `404` for a non-existent job id
- `GET /jobs?status=dead_lettered` → returns only dead-lettered jobs
- `GET /jobs?type=send_card_expiry_reminder` → returns only jobs of that type
