using DistributedJobQueue.Core.Domain;
using Dapper;
using Npgsql;
using System.Data;

namespace DistributedJobQueue.Core.Persistence
{
    public class PostgresStore : IJobStore
    {
        private readonly DbConnectionFactory dbfactory;

        public PostgresStore(DbConnectionFactory factory)
        {
            dbfactory = factory;
        }

        private async Task<T> WithDbHandlingAsync<T>(
            Func<IDbConnection, Task<T>> operation,
            string operationName)
        {
            await using var conn = dbfactory.CreateConnection();
            try
            {
                return await operation(conn);
            }
            catch (JobPersistenceException)
            {
                throw;
            }
            catch (PostgresException pgEx)
            {
                throw new JobPersistenceException($"{operationName} failed:\n" + pgEx.Message, pgEx);
            }
            catch (Exception ex)
            {
                throw new JobPersistenceException($"{operationName} failed:\n" + ex.Message, ex);
            }
        }

        public async Task<Guid> Ack(Guid jobId, string workerId, string resultUrl)
        {
            const string sql = @"
            UPDATE jobs SET status = @SuccessStatus, result_url = @ResultUrl
            WHERE id = @JobId AND leased_by = @WorkerId
            RETURNING id;";

            var parameters = new
            {
                JobId = jobId,
                WorkerId = workerId,
                ResultUrl = resultUrl,
                SuccessStatus = JobStatus.Succeeded.ToString()
            };

            return await WithDbHandlingAsync<Guid>(async conn =>
            {
                var result = await conn.ExecuteScalarAsync<Guid?>(sql, parameters);
                return result ?? throw new JobPersistenceException("Ack failed: no id returned");
            }, "Ack");
        }

        public async Task<Guid> Nack(Guid jobId, string workerId, string error)
        {
            const string sql = @"
            UPDATE jobs
            SET attempts = attempts + 1,
                status = CASE
                            WHEN attempts + 1 >= max_retries THEN @DeadLetteredStatus
                            ELSE @QueuedStatus
                         END,
                last_error = @Error,
                leased_by = NULL,
                lease_expires_at = NULL,
                run_after = CASE
                                WHEN attempts + 1 >= max_retries THEN run_after
                                ELSE now() + (LEAST(POWER(2, attempts), 60::double precision) + random() * 5) * interval '1 second'
                            END
            WHERE id = @JobId AND leased_by = @WorkerId
            RETURNING id AS ""JobId"";
            ";

            var parameters = new
            {
                JobId = jobId,
                WorkerId = workerId,
                Error = error,
                QueuedStatus = JobStatus.Queued.ToString(),
                DeadLetteredStatus = JobStatus.DeadLettered.ToString()
            };

            return await WithDbHandlingAsync<Guid>(async conn =>
            {
                var result = await conn.ExecuteScalarAsync<Guid?>(sql, parameters);
                return result ?? throw new JobPersistenceException("Nack failed: no id returned");
            }, "Nack");
        }

        public async Task<Guid> HeartBeat(Guid jobId, string workerId, TimeSpan leaseExtension)
        {
            const string sql = @"
            UPDATE jobs SET lease_expires_at  = now() + @LeaseExpiresAfter
            WHERE id = @JobId AND leased_by = @WorkerId AND status = @RunningStatus
            RETURNING id;";

            var parameters = new
            {
                JobId = jobId,
                WorkerId = workerId,
                LeaseExpiresAfter = leaseExtension,
                RunningStatus = JobStatus.Running.ToString()
            };

            return await WithDbHandlingAsync<Guid>(async conn =>
            {
                var result = await conn.ExecuteScalarAsync<Guid?>(sql, parameters);
                return result ?? throw new JobPersistenceException("HeartBeat failed: no id returned");
            }, "HeartBeat");
        }

        public async Task<LeasedJob?> Lease(string workerId, List<string> allowedTypes, TimeSpan leaseDuration)
        {
            const string sql = @"
            WITH updated AS (
                UPDATE jobs
                SET status = CASE
                                WHEN status = @RunningStatus AND attempts + 1 >= max_retries THEN @DeadLetteredStatus
                                ELSE @RunningStatus
                             END,
                    leased_by = @WorkerId,
                    lease_expires_at = now() + @LeaseDuration,
                    attempts = CASE WHEN status = @RunningStatus THEN attempts + 1 ELSE attempts END
                WHERE id = (
                    SELECT id FROM jobs
                    WHERE (status = @QueuedStatus AND type = ANY(@AllowedTypes) AND (run_after IS NULL OR run_after <= now()))
                       OR (status = @RunningStatus AND lease_expires_at < now() AND attempts < max_retries)
                    ORDER BY priority DESC, created_at ASC
                    FOR UPDATE SKIP LOCKED
                    LIMIT 1
                )
                RETURNING id, type, payload, attempts, lease_expires_at, status
            )
            SELECT
                id AS ""JobId"",
                type AS ""Type"",
                payload AS ""Payload"",
                attempts AS ""Attempt"",
                lease_expires_at AS ""LeaseExpiresAt""
            FROM updated
            WHERE status = @RunningStatus;
            ";

            var parameters = new
            {
                WorkerId = workerId,
                AllowedTypes = allowedTypes,
                LeaseDuration = leaseDuration,
                RunningStatus = JobStatus.Running.ToString(),
                QueuedStatus = JobStatus.Queued.ToString(),
                DeadLetteredStatus = JobStatus.DeadLettered.ToString(),
            };

            return await WithDbHandlingAsync<LeasedJob?>(async conn =>
            {
                return await conn.QuerySingleOrDefaultAsync<LeasedJob?>(sql, parameters);
            }, "Lease");
        }

        public async Task<Job?> GetJob(Guid id)
        {
            const string sql = @"
            SELECT
                id AS ""JobId"",
                type AS ""Type"",
                payload AS ""Payload"",
                status AS ""Status"",
                attempts AS ""Attempts"",
                max_retries AS ""MaxRetries"",
                priority AS ""Priority"",
                leased_by AS ""LeasedBy"",
                lease_expires_at AS ""LeaseExpiresAt"",
                run_after AS ""RunAfter"",
                result_url AS ""ResultUrl"",
                last_error AS ""LastError"",
                created_at AS ""CreatedAt"",
                finished_at AS ""FinishedAt""
            FROM jobs
            WHERE id = @Id";

            return await WithDbHandlingAsync<Job?>(async conn =>
            {
                return await conn.QuerySingleOrDefaultAsync<Job>(sql, new { Id = id });
            }, "GetJob");
        }

        public async Task<List<Job>> ListJobs(string? status, string? type)
        {
            const string sql = @"
            SELECT
                id AS ""JobId"",
                type AS ""Type"",
                payload AS ""Payload"",
                status AS ""Status"",
                attempts AS ""Attempts"",
                max_retries AS ""MaxRetries"",
                priority AS ""Priority"",
                leased_by AS ""LeasedBy"",
                lease_expires_at AS ""LeaseExpiresAt"",
                run_after AS ""RunAfter"",
                result_url AS ""ResultUrl"",
                last_error AS ""LastError"",
                created_at AS ""CreatedAt"",
                finished_at AS ""FinishedAt""
            FROM jobs
            WHERE (@Status IS NULL OR LOWER(status) = LOWER(@Status))
              AND (@Type IS NULL OR type = @Type)
            ORDER BY created_at DESC";

            var parameters = new { Status = status, Type = type };

            return await WithDbHandlingAsync<List<Job>>(async conn =>
            {
                var result = await conn.QueryAsync<Job>(sql, parameters);
                return result.AsList();
            }, "ListJobs");
        }

        public async Task Replay(Guid jobId)
        {
            const string sql = @"
            UPDATE jobs
            SET status = @QueuedStatus,
                attempts = 0,
                last_error = NULL,
                leased_by = NULL,
                lease_expires_at = NULL,
                run_after = NULL
            WHERE id = @JobId
              AND status = @DeadLetteredStatus
            RETURNING id;";

            var parameters = new
            {
                JobId = jobId,
                QueuedStatus = JobStatus.Queued.ToString(),
                DeadLetteredStatus = JobStatus.DeadLettered.ToString()
            };

            await WithDbHandlingAsync<int>(async conn =>
            {
                var result = await conn.ExecuteAsync(sql, parameters);
                if (result == 0)
                {
                    var exists = await conn.ExecuteScalarAsync<int?>(
                        "SELECT COUNT(*) FROM jobs WHERE id = @Id",
                        new { Id = jobId });
                    if (exists == 0)
                        throw new JobPersistenceException($"Job {jobId} not found");
                    throw new JobPersistenceException($"Job {jobId} is not in dead_lettered status");
                }
                return result;
            }, "Replay");
        }

        public async Task<Guid> Save(NewJobRequest request)
        {
            const string sql = @"
            INSERT INTO jobs (type, payload, status, priority, attempts, max_retries, idempotency_key, run_after)
            VALUES (@Type, @Payload::jsonb, @Status, @Priority, @Attempts, @MaxRetries, @IdempotencyKey, now() + @RunAfter)
            RETURNING id;";

            var parameters = new
            {
                request.Type,
                request.Payload,
                Status = JobStatus.Queued.ToString(),
                request.Priority,
                Attempts = 0,
                request.MaxRetries,
                request.IdempotencyKey,
                request.RunAfter
            };

            // Provide a Postgres-specific handler to resolve idempotency violation using the same connection.
            return await WithDbHandlingAsync<Guid>(async conn =>
            {
                try
                {
                    var result = await conn.ExecuteScalarAsync<Guid?>(sql, parameters);
                    return result ?? throw new JobPersistenceException("Insert failed: no id returned");
                }
                catch (PostgresException pgEx) when (pgEx.SqlState == PostgresErrorCodes.UniqueViolation && pgEx.ConstraintName == "jobs_idempotency_key_key")
                {
                    var existingId = await conn.ExecuteScalarAsync<Guid?>(
                        "SELECT id FROM jobs WHERE idempotency_key = @IdempotencyKey",
                        new { request.IdempotencyKey });
                    return existingId ?? throw new JobPersistenceException("Idempotency conflict but existing row not found");
                }
            }, "Insert");
        }
    }
}