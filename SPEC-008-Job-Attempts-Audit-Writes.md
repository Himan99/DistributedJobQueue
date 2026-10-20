# SPEC-008 — Job Attempts Audit Writes

**ID:** SPEC-008  
**Title:** Write audit records to job_attempts table  
**Status:** Implemented  
**Type:** Enhancement  
**Architecture ref:** Section 4 — `job_attempts` table (append-only history for debugging + DLQ inspection)

## Problem
The `job_attempts` table exists in the database schema (`db/JobQueueDbInit.sql`) but no C# code writes to it. There is no audit trail of job attempts, workers, outcomes, or errors.

## Requirements
1. On each job lease (start of an attempt), insert a row into `job_attempts`:
   - `job_id`, `attempt_num`, `worker_id`, `outcome`, `started_at`
2. On job ack, update the row: `outcome = 'succeeded'`, `finished_at = now()`.
3. On job nack, update the row: `outcome = 'failed'`, `error = <error message>`, `finished_at = now()`.
4. `attempt_num` corresponds to the job's `attempts` value at the time of the attempt.

## Files affected
- `BrokerService/Persistence/IJobStore.cs` — add audit method(s) or integrate into existing lease/ack/nack
- `BrokerService/Persistence/PostgresStore.cs` — INSERT on lease, UPDATE on ack/nack against `job_attempts`

## Verification
- Each lease creates a `job_attempts` row with the correct `job_id`, `attempt_num`, `worker_id`, `started_at`
- Ack sets `outcome = 'succeeded'` and `finished_at`
- Nack sets `outcome = 'failed'`, `error`, and `finished_at`
- Audit rows can be queried for DLQ inspection and debugging
