# SPEC-002 — Comment/Code Mismatch Fix

**ID:** SPEC-002  
**Title:** Correct stale comment in NackRetryTest  
**Status:** Implemented  
**Priority:** Low  
**Type:** Documentation fix

## Problem
The comment in `NackRetryTest` stated `run_after = now()+30s` but the actual SQL in `PostgresStore.Nack` uses `interval '1 seconds'`. The stale comment misleads readers about the backoff duration.

## Change
Updated the comment in `PostgresStoreTest.cs` (`NackRetryTest`) from `now()+30s` to `now()+1s` to match the SQL.

### Files affected
| File | Line | Change |
|------|------|--------|
| `DistributedJobQueueBrokerService/PostgresStoreTest.cs` | ~130 | `run_after = now()+30s` → `run_after = now()+1s` |

### SQL reference (unchanged)
`BrokerService/Persistence/PostgresStore.cs:76` — `run_after = CASE WHEN ... ELSE now() + interval '1 seconds' END`

## Verification
- Comment now matches the actual `Nack` SQL backoff value.
- No behavior change; documentation-only fix.
