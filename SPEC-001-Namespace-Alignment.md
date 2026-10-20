# SPEC-001 — Namespace Alignment

**ID:** SPEC-001  
**Title:** Align C# namespaces with assembly/project name  
**Status:** Implemented  
**Priority:** Low  
**Type:** Internal refactoring

## Problem
The `DistributedJobQueue.Core` project (assembly) uses namespaces `DistributedJobQueueCore.*` (no dot), creating an inconsistent naming convention between the assembly name and the code namespaces.

## Change
Renamed all `DistributedJobQueueCore.*` namespaces to `DistributedJobQueue.Core.*` to match the project/assembly name.

### Files affected
| File | Change |
|------|--------|
| `BrokerService/Domain/Job.cs` | Namespace `DistributedJobQueueCore.Domain` → `DistributedJobQueue.Core.Domain`; removed self-referential `using` |
| `BrokerService/Domain/LeasedJob.cs` | Namespace `DistributedJobCore.Domain` → `DistributedJobQueue.Core.Domain` |
| `BrokerService/Domain/NewJobRequest.cs` | Namespace `DistributedJobQueueCore.Domain` → `DistributedJobQueue.Core.Domain` |
| `BrokerService/Domain/JobStatus.cs` | Namespace `DistributedJobQueueCore.Domain` → `DistributedJobQueue.Core.Domain` |
| `BrokerService/Domain/JobPersistenceException.cs` | Namespace `DistributedJobQueueCore.Domain` → `DistributedJobQueue.Core.Domain` |
| `BrokerService/Persistence/IJobStore.cs` | Namespace `DistributedJobQueueCore.Persistence` → `DistributedJobQueue.Core.Persistence`; updated `using` |
| `BrokerService/Persistence/DbConnectionFactory.cs` | Namespace `DistributedJobQueueCore.Persistence` → `DistributedJobQueue.Core.Persistence` |
| `BrokerService/Persistence/PostgresStore.cs` | Namespace `DistributedJobQueueCore.Persistence` → `DistributedJobQueue.Core.Persistence`; updated `using` |
| `DistributedJobQueue.Api/Controllers/JobsController.cs` | Updated 2 `using` statements |
| `DistributedJobQueue.Api/Program.cs` | Updated `using` statement |
| `DistributedJobQueueBrokerService/Program.cs` | Updated `using` statement |
| `DistributedJobQueueBrokerService/PostgresStoreTest.cs` | Updated 2 `using` statements |

## Verification
- No `DistributedJobQueueCore` references remain in source files (only in `obj/` build artifacts, regenerated on build).
- Build compiles successfully.
