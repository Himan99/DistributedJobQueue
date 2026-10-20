# CLAUDE.md — DistributedJobQueue Testing & Development Procedures

## Overview

This document describes the standard testing and development workflow for the DistributedJobQueue project.

## Project Structure

- `BrokerService/DistributedJobQueue.Core.csproj` — Core library (domain models, persistence)
- `DistributedJobQueue.Api/DistributedJobQueue.Api.csproj` — ASP.NET Core minimal API
- `DistributedJobQueueBrokerService/DistributedJobQueue.ConsoleTester.csproj` — Console tester with integration tests
- `db/JobQueueDbInit.sql` — Database schema (jobs + job_attempts tables)
- `docker-compose.yaml` — PostgreSQL 18 database
- `scripts/initdbfortests.sh` — Reset jobs table for testing

## Database

PostgreSQL 18 runs in Docker container `dist_job_queue` on port 5432.

Connection string: `Host=localhost;Port=5432;Database=job_queue_db;Username=job_queue_user;Password=job_queue_pass`

### initdbfortests — Reset database for testing

Before running ConsoleTester tests, reset the `jobs` table to a clean state:

```bash
./scripts/initdbfortests.sh
```

This script runs `DELETE FROM jobs;` against the database. This is the expected initial condition for ConsoleTester tests — each test creates jobs with unique types, so a clean jobs table ensures test isolation.

## ConsoleTester

The ConsoleTester project (`DistributedJobQueueBrokerService/`) runs 10 integration tests as an `IHostedService` on startup:

1. AckSuccessTest
2. AckWrongWorkerTest
3. NackRetryTest
4. NackExhaustsRetriesTest
5. VisibiltyTestAsync
6. ExpiredLeaseTest
7. OrphanExhaustsRetriesTest
8. ConcurrentLeaseNoDuplicatesTest
9. IdempotencyKeyDedupeTest
10. HeartbeatExtendsLeaseTest

### Running ConsoleTester tests

1. Ensure PostgreSQL is running: `docker ps` (should show `dist_job_queue`)
2. Reset the database: `./scripts/initdbfortests.sh`
3. Run the tester from the project directory:
   ```bash
   cd DistributedJobQueueBrokerService
   dotnet run --project DistributedJobQueue.ConsoleTester.csproj
   ```

Note: The host runs indefinitely after tests complete. Press Ctrl+C to shut down. Test results are printed to stdout.

## SpecFlow Workflow — Test After Each Implementation

After implementing each spec (SPEC-001 through SPEC-009):

1. **Implement** the spec changes
2. **Build**: `dotnet build DistributedJobQueueBrokerService/DistributedJobQueueBrokerService.sln` — must have 0 warnings, 0 errors
3. **Reset DB**: `./scripts/initdbfortests.sh`
4. **Run tests**: Run ConsoleTester — all 10 tests must pass
5. **Commit**: If tests pass, create a new branch, commit changes
6. **Merge**: Merge to master only if all tests pass

If any test fails, do not merge to master. Fix the issues and re-test.

## Git Workflow

1. Create a feature branch: `git checkout -b feature/spec-NNN`
2. Implement changes
3. Run tests (build + ConsoleTester)
4. If tests pass: `git add .` and `git commit`
5. Merge to master: `git checkout master && git merge feature/spec-NNN`
6. If tests fail: do not merge; fix and re-test

## Current Spec Status

| Spec | Title | Status |
|------|-------|--------|
| SPEC-001 | Namespace Alignment | Implemented |
| SPEC-002 | Comment/Code Mismatch | Implemented |
| SPEC-003 | Get / List Jobs | Implemented |
| SPEC-004 | DLQ Replay | Implemented |
| SPEC-005 | Exponential Backoff | Implemented |
| SPEC-006 | Worker Service | Implemented |
| SPEC-007 | Worker Heartbeat Thread | Implemented |
| SPEC-008 | Job Attempts Audit Writes | Implemented |
| SPEC-009 | Pool Isolation (Fast/Batch) | Implemented |

## Prerequisites

- .NET 8 SDK
- Docker (for PostgreSQL)
- PostgreSQL 18 container `dist_job_queue` running on port 5432