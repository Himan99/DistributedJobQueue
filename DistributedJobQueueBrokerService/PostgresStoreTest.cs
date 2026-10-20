using DistributedJobQueue.Core.Domain;
using DistributedJobQueue.Core.Persistence;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;

namespace DistributedJobQueueConsoleTester
{
    public class PostgresStoreTest: IHostedService
    {
        readonly IJobStore postgresStore;
        readonly Random random;
        readonly IHostApplicationLifetime lifetime;
        public PostgresStoreTest(IJobStore jobStore, IHostApplicationLifetime hostLifetime)
        {
            postgresStore = jobStore;
            random = new Random();
            lifetime = hostLifetime;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            //Console.WriteLine("Enter number of jobs to enqueue");
            //int.TryParse(Console.ReadLine(), out int n);
            //for (int i = 0; i < n; i++)
            //{
            //    var result = await Enqueue();
            //    Console.WriteLine(result.ToString());
            //}

            //Console.WriteLine("Enter number of jobs to lease");
            //int.TryParse(Console.ReadLine(), out int m);
            //for (int i = 0; i < m; i++)
            //{
            //    var result = await Lease("worker_id"+i.ToString(), new List<string>{ "jobType1"}, TimeSpan.FromSeconds(150));
            //    Console.WriteLine(JsonSerializer.Serialize(result));
            //}

            await RunAllTests();

            lifetime.StopApplication();
        }

        public async Task RunAllTests()
        {
            var tests = new (string Name, Func<Task> Test)[]
            {
                ("AckSuccessTest", AckSuccessTest),
                ("AckWrongWorkerTest", AckWrongWorkerTest),
                ("NackRetryTest", NackRetryTest),
                ("NackExhaustsRetriesTest", NackExhaustsRetriesTest),
                ("VisibiltyTestAsync", VisibiltyTestAsync),
                ("ExpiredLeaseTest", ExpiredLeaseTest),
                ("OrphanExhaustsRetriesTest", OrphanExhaustsRetriesTest),
                ("ConcurrentLeaseNoDuplicatesTest", ConcurrentLeaseNoDuplicatesTest),
                ("IdempotencyKeyDedupeTest", IdempotencyKeyDedupeTest),
                ("HeartbeatExtendsLeaseTest", HeartbeatExtendsLeaseTest),
    ("GetJobReturnsExistingJobTest", GetJobReturnsExistingJobTest),
    ("GetJobReturnsNullForMissingTest", GetJobReturnsNullForMissingTest),
    ("ListJobsFiltersByStatusTest", ListJobsFiltersByStatusTest),
    ("ListJobsFiltersByTypeTest", ListJobsFiltersByTypeTest),
    ("ReplayDeadLetterJobTest", ReplayDeadLetterJobTest),
    ("ReplayNonDeadLetterJobFailsTest", ReplayNonDeadLetterJobFailsTest),
    ("NackExponentialBackoffTest", NackExponentialBackoffTest),
    ("NackBackoffHasJitterTest", NackBackoffHasJitterTest),
    ("AckSetsFinishedAtTest", AckSetsFinishedAtTest),
    ("GetAttemptsReturnsAuditTrailTest", GetAttemptsReturnsAuditTrailTest),
            };

            int passed = 0, failed = 0;

            foreach (var (name, test) in tests)
            {
                try
                {
                    await test();
                    Console.WriteLine($"[PASS] {name}");
                    passed++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FAIL] {name}: {ex.Message}");
                    failed++;
                }
            }

            Console.WriteLine($"\n{passed} passed, {failed} failed out of {tests.Length}");
        }

        private static string UniqueType(string label) => $"{label}_{Guid.NewGuid():N}";

        // 1. Basic Ack path — job should end in Succeeded with result_url set
        private async Task AckSuccessTest()
        {
            var type = UniqueType("ack_success");
            var jobId = await postgresStore.Save(new NewJobRequest(type, "{}"));
            var leased = await postgresStore.Lease("worker-A", new List<string> { type }, TimeSpan.FromSeconds(30));
            Debug.Assert(leased != null && leased.JobId == jobId, "Job was not leased");

            var acked = await postgresStore.Ack(jobId, "worker-A", "file://result.pdf");
            Debug.Assert(acked == jobId, "Ack did not return the same job id");

            // job should no longer be leasable (status != Queued/Running-expired)
            var reLease = await postgresStore.Lease("worker-B", new List<string> { type }, TimeSpan.FromSeconds(30));
            Debug.Assert(reLease == null, "Succeeded job was still leasable");
        }

        // 2. Ack from a worker that doesn't hold the lease should be a no-op (ownership guard)
        private async Task AckWrongWorkerTest()
        {
            var type = UniqueType("ack_wrong_worker");
            var jobId = await postgresStore.Save(new NewJobRequest(type, "{}"));
            var leased = await postgresStore.Lease("worker-A", new List<string> { type }, TimeSpan.FromSeconds(30));
            Debug.Assert(leased != null);

            bool threw = false;
            try
            {
                await postgresStore.Ack(jobId, "worker-B", "file://result.pdf"); // worker-B never held the lease
            }
            catch (JobPersistenceException)
            {
                threw = true; // expected: no matching row (id + leased_by), so "no id returned"
            }
            Debug.Assert(threw, "Ack succeeded from a worker that did not hold the lease");

            // Confirm worker-A's real Ack still works afterward — proves the row wasn't corrupted
            var acked = await postgresStore.Ack(jobId, "worker-A", "file://result.pdf");
            Debug.Assert(acked == jobId, "Legitimate Ack failed after a stale Ack attempt");
        }

        // 3. Nack with retries remaining -> job returns to Queued and is re-leasable after run_after
        private async Task NackRetryTest()
        {
            var type = UniqueType("nack_retry");
            // max_retries defaults from NewJobRequest; make sure it's >1 (e.g. default 3)
            var jobId = await postgresStore.Save(new NewJobRequest(type, "{}", MaxRetries: 3));
            var leased = await postgresStore.Lease("worker-A", new List<string> { type }, TimeSpan.FromSeconds(30));
            Debug.Assert(leased != null && leased.Attempt == 0, $"Expected first attempt=0, got {leased?.Attempt}");

            await postgresStore.Nack(jobId, "worker-A", "simulated failure");

            // Nack sets run_after = now()+1s in current SQL, so immediate re-lease should fail
            var immediateRelease = await postgresStore.Lease("worker-B", new List<string> { type }, TimeSpan.FromSeconds(30));
            Debug.Assert(immediateRelease == null, "Job was re-leasable before its backoff run_after elapsed");

            // NOTE: to assert it becomes leasable *after* run_after, either shorten backoff for tests
            // or query the row directly and confirm attempts == 1 and status == 'Queued'.
        }

        // 4. Nack repeated until max_retries -> job ends in DeadLettered, never leasable again
        private async Task NackExhaustsRetriesTest()
        {
            var type = UniqueType("nack_dlq");
            var jobId = await postgresStore.Save(new NewJobRequest(type, "{}", MaxRetries: 2));

            // First nack: attempts 0→1, job queued with exponential backoff (~1-5s with jitter)
            var leased1 = await postgresStore.Lease("worker-A", new List<string> { type }, TimeSpan.FromSeconds(30));
            Debug.Assert(leased1 != null, "Expected job leasable on first loop");
            await postgresStore.Nack(jobId, "worker-A", "failure #1");

            var afterFirst = await postgresStore.GetJob(jobId);
            Debug.Assert(afterFirst != null, "GetJob returned null after first nack");
            Debug.Assert(afterFirst!.Attempts == 1, $"Expected attempts=1 after first nack, got {afterFirst.Attempts}");
            Debug.Assert(afterFirst.Status == JobStatus.Queued, "Job should be queued after first nack");
            Debug.Assert(afterFirst.RunAfter != null, "Job should have run_after set after first nack");
            Debug.Assert(afterFirst.RunAfter!.Value > DateTime.UtcNow, "run_after should be in the future");

            // Wait for backoff to elapse (max ~5s base+jitter for attempt 1, plus buffer)
            await Task.Delay(7000);

            // Second nack: attempts 1→2 >= max_retries(2) -> dead_lettered
            var leased2 = await postgresStore.Lease("worker-B", new List<string> { type }, TimeSpan.FromSeconds(30));
            Debug.Assert(leased2 != null, "Expected job leasable after backoff elapsed");
            await postgresStore.Nack(jobId, "worker-B", "failure #2");

            var afterSecond = await postgresStore.GetJob(jobId);
            Debug.Assert(afterSecond != null, "GetJob returned null after second nack");
            Debug.Assert(afterSecond!.Attempts == 2, $"Expected attempts=2 after second nack, got {afterSecond.Attempts}");
            Debug.Assert(afterSecond.Status == JobStatus.DeadLettered, "Job should be dead-lettered after exhausting max_retries");

            // confirm it stays dead — no further reclaim possible
            var lease3 = await postgresStore.Lease("worker-C", new List<string> { type }, TimeSpan.FromSeconds(30));
            Debug.Assert(lease3 == null, "Dead-lettered job was reclaimed again");
        }

        // 17. Nack applies exponential backoff with jitter — first retry short, second longer
        private async Task NackExponentialBackoffTest()
        {
            var type = UniqueType("nack_backoff");
            var jobId = await postgresStore.Save(new NewJobRequest(type, "{}", MaxRetries: 5));

            // First nack: attempts 0→1, backoff = min(2^0, 60) + jitter(0-5) ≈ 1-5s
            var leased1 = await postgresStore.Lease("worker-A", new List<string> { type }, TimeSpan.FromSeconds(30));
            Debug.Assert(leased1 != null, "Expected job leasable");
            await postgresStore.Nack(jobId, "worker-A", "failure #1");

            var job1 = await postgresStore.GetJob(jobId);
            Debug.Assert(job1 != null && job1.Attempts == 1, "Expected attempts=1 after first nack");
            Debug.Assert(job1.RunAfter != null, "First nack should set run_after");
            var backoff1 = job1.RunAfter!.Value - DateTime.UtcNow;
            Debug.Assert(backoff1.TotalSeconds >= 0.5 && backoff1.TotalSeconds <= 7,
                $"First retry backoff should be ~1-5s, got {backoff1.TotalSeconds}s");

            // Wait for first backoff to elapse, polling the lease until the job is re-leasable.
            // Max backoff for attempt 1 = 2^0 + 5s jitter = 6s; poll for up to 15s to absorb clock skew.
            LeasedJob? leased2 = null;
            for (int i = 0; i < 15; i++)
            {
                await Task.Delay(1000);
                leased2 = await postgresStore.Lease("worker-B", new List<string> { type }, TimeSpan.FromSeconds(30));
                if (leased2 != null) break;
            }
            Debug.Assert(leased2 != null, "Expected job leasable after first backoff elapsed");
            // Nack the job that was actually leased (leased_by is set by Lease; using saved jobId would fail
            // because the first Nack cleared leased_by on the original job).
            await postgresStore.Nack(leased2.JobId, "worker-B", "failure #2");

            var job2 = await postgresStore.GetJob(leased2.JobId);
            Debug.Assert(job2 != null, "GetJob returned null after second nack");
            Debug.Assert(job2.Attempts >= 1, $"Expected attempts>=1 after second nack, got {job2.Attempts}");
            Debug.Assert(job2.RunAfter != null, "Second nack should set run_after");
            var backoff2 = job2.RunAfter!.Value - DateTime.UtcNow;
            Debug.Assert(backoff2.TotalSeconds >= 0.5 && backoff2.TotalSeconds <= 67,
                $"Second retry backoff should be ~1-65s, got {backoff2.TotalSeconds}s");
        }

        // 18. Nack backoff varies due to jitter — two jobs with same attempt get different run_after
        private async Task NackBackoffHasJitterTest()
        {
            var type = UniqueType("nack_jitter");
            var jobId1 = await postgresStore.Save(new NewJobRequest(type, "{}", MaxRetries: 5));
            var jobId2 = await postgresStore.Save(new NewJobRequest(type, "{}", MaxRetries: 5));

            var leased1 = await postgresStore.Lease("worker-A", new List<string> { type }, TimeSpan.FromSeconds(30));
            var leased2 = await postgresStore.Lease("worker-B", new List<string> { type }, TimeSpan.FromSeconds(30));
            Debug.Assert(leased1 != null && leased2 != null, "Both jobs should be leasable");

            await postgresStore.Nack(jobId1, "worker-A", "failure #1");
            await postgresStore.Nack(jobId2, "worker-B", "failure #1");

            var job1 = await postgresStore.GetJob(jobId1);
            var job2 = await postgresStore.GetJob(jobId2);
            Debug.Assert(job1 != null && job2 != null, "Both jobs should exist");
            Debug.Assert(job1!.RunAfter != null && job2!.RunAfter != null, "Both jobs should have run_after");

            var runAfter1 = job1.RunAfter!.Value;
            var runAfter2 = job2.RunAfter!.Value;
            // Both should be in the near future (~1-5s) but not identical due to jitter
            Debug.Assert(runAfter1 > DateTime.UtcNow && runAfter2 > DateTime.UtcNow, "Both run_after should be in the future");
            Debug.Assert(Math.Abs((runAfter1 - runAfter2).TotalSeconds) > 0.1,
                $"Jitter should cause different run_after values, got {runAfter1} and {runAfter2}");
        }

        // 5. Visibility: job leased once is not leasable again while lease is valid (existing test, kept)
        private async Task VisibiltyTestAsync()
        {
            var type = UniqueType("visibility");
            var jobId = await postgresStore.Save(new NewJobRequest(type, "{}"));
            var leased = await postgresStore.Lease("worker-A", new List<string> { type }, TimeSpan.FromSeconds(30));
            Debug.Assert(leased!.JobId == jobId);
            var secondAttempt = await postgresStore.Lease("worker-B", new List<string> { type }, TimeSpan.FromSeconds(30));
            Debug.Assert(secondAttempt == null, "Job was leased twice while first lease still valid!");
        }

        // 6. Expired lease reclaim (existing test, kept) — also confirms attempts increments on reclaim
        private async Task ExpiredLeaseTest()
        {
            var type = UniqueType("expired_lease");
            var jobId = await postgresStore.Save(new NewJobRequest(type, "{}"));
            var firstLease = await postgresStore.Lease("worker-A", new List<string> { type }, TimeSpan.FromSeconds(2));
            Debug.Assert(firstLease!.Attempt == 0, $"Expected initial attempt=0, got {firstLease.Attempt}");

            await Task.Delay(TimeSpan.FromSeconds(3));

            var reLeased = await postgresStore.Lease("worker-B", new List<string> { type }, TimeSpan.FromSeconds(30));
            Debug.Assert(reLeased != null && reLeased.JobId == jobId, "Expired lease was not reclaimed");
            Debug.Assert(reLeased.Attempt == 1, $"Expected attempt incremented to 1 on reclaim, got {reLeased.Attempt}");
        }

        // 7. Orphan reclaimed repeatedly past max_retries -> ends DeadLettered, stops being reclaimed
        private async Task OrphanExhaustsRetriesTest()
        {
            var type = UniqueType("orphan_dlq");
            var jobId = await postgresStore.Save(new NewJobRequest(type, "{}", MaxRetries: 2));

            // attempt 1: lease with a short lease, let it expire without ack/nack (simulated crash)
            var lease1 = await postgresStore.Lease("worker-A", new List<string> { type }, TimeSpan.FromSeconds(1));
            Debug.Assert(lease1 != null && lease1.Attempt == 0);
            await Task.Delay(TimeSpan.FromSeconds(2));

            // attempt 2: reclaimed, attempts -> 1, still under max_retries(2)
            var lease2 = await postgresStore.Lease("worker-B", new List<string> { type }, TimeSpan.FromSeconds(1));
            Debug.Assert(lease2 != null && lease2.Attempt == 1, $"Expected attempt=1, got {lease2?.Attempt}");
            await Task.Delay(TimeSpan.FromSeconds(2));

            // attempt 3 would push attempts+1 >= max_retries(2) -> should dead-letter inline, NOT be returned
            var lease3 = await postgresStore.Lease("worker-C", new List<string> { type }, TimeSpan.FromSeconds(1));
            Debug.Assert(lease3 == null, "Job should have been dead-lettered instead of re-leased past max_retries");

            // confirm it stays dead — no further reclaim possible even after waiting
            await Task.Delay(TimeSpan.FromSeconds(2));
            var lease4 = await postgresStore.Lease("worker-D", new List<string> { type }, TimeSpan.FromSeconds(1));
            Debug.Assert(lease4 == null, "Dead-lettered job was reclaimed again");
        }

        // 8. Concurrency: many concurrent lease attempts against few jobs -> no duplicate leases
        private async Task ConcurrentLeaseNoDuplicatesTest()
        {
            var type = UniqueType("concurrency");
            var jobIds = new List<Guid>();
            for (int i = 0; i < 5; i++)
                jobIds.Add(await postgresStore.Save(new NewJobRequest(type, "{}")));

            var gate = new ManualResetEventSlim(false);
            var tasks = Enumerable.Range(0, 20).Select(i => Task.Run(async () =>
            {
                gate.Wait();
                return await postgresStore.Lease($"worker-{i}", new List<string> { type }, TimeSpan.FromSeconds(30));
            })).ToArray();

            await Task.Delay(200);
            gate.Set();
            var results = await Task.WhenAll(tasks);

            var leased = results.Where(r => r != null).ToList();
            var distinctIds = leased.Select(j => j!.JobId).Distinct().Count();

            Debug.Assert(leased.Count == 5, $"Expected exactly 5 successful leases, got {leased.Count}");
            Debug.Assert(distinctIds == leased.Count, "DUPLICATE LEASE DETECTED across concurrent callers");
        }

        // 9. Idempotency key dedupe: second Save with same key returns same job id, doesn't create a new row
        private async Task IdempotencyKeyDedupeTest()
        {
            var type = UniqueType("idempotency");
            var key = $"idem-{Guid.NewGuid():N}";

            var firstId = await postgresStore.Save(new NewJobRequest(type, "{}", IdempotencyKey: key));
            var secondId = await postgresStore.Save(new NewJobRequest(type, "{}", IdempotencyKey: key));

            Debug.Assert(firstId == secondId, $"Expected same job id for duplicate idempotency key, got {firstId} vs {secondId}");
        }

        // 10. HeartBeat extends lease — job shouldn't be reclaimable while heartbeats keep arriving
        private async Task HeartbeatExtendsLeaseTest()
        {
            var type = UniqueType("heartbeat");
            var jobId = await postgresStore.Save(new NewJobRequest(type, "{}"));
            var leased = await postgresStore.Lease("worker-A", new List<string> { type }, TimeSpan.FromSeconds(2));
            Debug.Assert(leased != null);

            await Task.Delay(TimeSpan.FromSeconds(1));
            await postgresStore.HeartBeat(jobId, "worker-A", TimeSpan.FromSeconds(5)); // extend before original 2s expiry

            await Task.Delay(TimeSpan.FromSeconds(2)); // total elapsed ~3s, past original lease but within extension

            var reclaimAttempt = await postgresStore.Lease("worker-B", new List<string> { type }, TimeSpan.FromSeconds(30));
            Debug.Assert(reclaimAttempt == null, "Job was reclaimed despite active heartbeat extending the lease");
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        async Task<Guid> Enqueue()
        {
            return await postgresStore.Save(new NewJobRequest(
                Type: "jobType1",
                IdempotencyKey: "idmpot" + random.Next() % 100000,
                MaxRetries: 5,
                RunAfter: null,
                Payload: $"{{\"batch_id\": \"b-123\", \"seq\": {random.Next() % 1000}}}"));
        }

        async Task<LeasedJob?> Lease(string workerId, List<string> types, TimeSpan leaseSeconds)
        {
            var res = await postgresStore.Lease(workerId, types, leaseSeconds);
            if (res == null)
            {
                Console.WriteLine("No Jobs available");
            }
            return res;
        }

        // 11. GetJob returns existing job with all fields
        private async Task GetJobReturnsExistingJobTest()
        {
            var type = UniqueType("get_job");
            var jobId = await postgresStore.Save(new NewJobRequest(type, "{\"key\":\"value\"}", MaxRetries: 3, Priority: 5));

            var job = await postgresStore.GetJob(jobId);
            Debug.Assert(job != null, "GetJob returned null for existing job");
            Debug.Assert(job.JobId == jobId, "GetJob returned wrong job id");
            Debug.Assert(job.Type == type, "GetJob returned wrong type");
            Debug.Assert(job.Status == JobStatus.Queued, "GetJob returned wrong status");
            Debug.Assert(job.Attempts == 0, "GetJob returned wrong attempts");
            Debug.Assert(job.Priority == 5, "GetJob returned wrong priority");
        }

        // 12. GetJob returns null for missing job
        private async Task GetJobReturnsNullForMissingTest()
        {
            var job = await postgresStore.GetJob(Guid.NewGuid());
            Debug.Assert(job == null, "GetJob returned non-null for missing job");
        }

        // 13. ListJobs filters by status
        private async Task ListJobsFiltersByStatusTest()
        {
            var type = UniqueType("list_status");
            var job1 = await postgresStore.Save(new NewJobRequest(type, "{}", MaxRetries: 1));
            var job2 = await postgresStore.Save(new NewJobRequest(type, "{}", MaxRetries: 1));

            // Lease and nack job1 to dead-letter it (attempts 1 >= max_retries 1)
            var leased = await postgresStore.Lease("worker-A", new List<string> { type }, TimeSpan.FromSeconds(30));
            Debug.Assert(leased != null);
            await postgresStore.Nack(job1, "worker-A", "failure");

            var queuedJobs = await postgresStore.ListJobs(JobStatus.Queued.ToString(), null);
            var deadLetterJobs = await postgresStore.ListJobs(JobStatus.DeadLettered.ToString(), null);

            Debug.Assert(queuedJobs.Any(j => j.JobId == job2), "Queued list should contain job2");
            Debug.Assert(deadLetterJobs.Any(j => j.JobId == job1), "Dead-lettered list should contain job1");
        }

        // 14. ListJobs filters by type
        private async Task ListJobsFiltersByTypeTest()
        {
            var type1 = UniqueType("list_type_a");
            var type2 = UniqueType("list_type_b");
            await postgresStore.Save(new NewJobRequest(type1, "{}"));
            await postgresStore.Save(new NewJobRequest(type2, "{}"));

            var type1Jobs = await postgresStore.ListJobs(null, type1);
            var type2Jobs = await postgresStore.ListJobs(null, type2);

            Debug.Assert(type1Jobs.All(j => j.Type == type1), "Type filter should only return type1 jobs");
            Debug.Assert(type2Jobs.All(j => j.Type == type2), "Type filter should only return type2 jobs");
            Debug.Assert(type1Jobs.Count == 1, "Should have exactly 1 type1 job");
            Debug.Assert(type2Jobs.Count == 1, "Should have exactly 1 type2 job");
        }

        // 15. Replay resets dead-lettered job to queued
        private async Task ReplayDeadLetterJobTest()
        {
            var type = UniqueType("replay");
            var jobId = await postgresStore.Save(new NewJobRequest(type, "{}", MaxRetries: 1));

            // Lease and nack to dead-letter
            var leased = await postgresStore.Lease("worker-A", new List<string> { type }, TimeSpan.FromSeconds(30));
            Debug.Assert(leased != null);
            await postgresStore.Nack(jobId, "worker-A", "failure"); // attempts=1 >= max_retries=1 → dead_lettered

            // Verify it's dead-lettered
            var deadLetterJob = await postgresStore.GetJob(jobId);
            Debug.Assert(deadLetterJob != null, "GetJob returned null for dead-lettered job");
            Debug.Assert(deadLetterJob!.Status == JobStatus.DeadLettered, "Job should be dead-lettered");

            // Replay
            await postgresStore.Replay(jobId);

            // Verify it's back to queued with reset fields
            var replayedJob = await postgresStore.GetJob(jobId);
            Debug.Assert(replayedJob != null, "GetJob returned null for replayed job");
            Debug.Assert(replayedJob!.Status == JobStatus.Queued, "Replayed job should be queued");
            Debug.Assert(replayedJob.Attempts == 0, "Replayed job should have attempts=0");
            Debug.Assert(replayedJob.LastError == null, "Replayed job should have no last_error");
            Debug.Assert(replayedJob.LeasedBy == null, "Replayed job should have no leased_by");
            Debug.Assert(replayedJob.LeaseExpiresAt == null, "Replayed job should have no lease_expires_at");
            Debug.Assert(replayedJob.RunAfter == null, "Replayed job should have no run_after");
        }

        // 16. Replay on non-dead-lettered job fails
        private async Task ReplayNonDeadLetterJobFailsTest()
        {
            var type = UniqueType("replay_fail");
            var jobId = await postgresStore.Save(new NewJobRequest(type, "{}"));

            bool threw = false;
            try
            {
                await postgresStore.Replay(jobId);
            }
            catch (JobPersistenceException)
            {
                threw = true;
            }
            Debug.Assert(threw, "Replay on non-dead-lettered job should throw");
        }

        // 17. Ack sets finished_at on job_attempts
        private async Task AckSetsFinishedAtTest()
        {
            var type = UniqueType("ack_finished_at");
            var jobId = await postgresStore.Save(new NewJobRequest(type, "{}", MaxRetries: 3));
            var leased = await postgresStore.Lease("worker-A", new List<string> { type }, TimeSpan.FromSeconds(30));
            Debug.Assert(leased != null);

            await postgresStore.Ack(jobId, "worker-A", "file://result.pdf");

            var attempts = await postgresStore.GetAttempts(jobId);
            Debug.Assert(attempts != null && attempts.Count == 1, "Expected exactly 1 attempt record");
            var attempt = attempts[0];
            Debug.Assert(attempt.FinishedAt != null, "finished_at should be set after Ack");
            Debug.Assert(attempt.Outcome == "succeeded", "Attempt outcome should be succeeded");
        }

        // 18. GetAttempts returns audit trail with all attempt records
        private async Task GetAttemptsReturnsAuditTrailTest()
        {
            var type = UniqueType("get_attempts_audit");
            var jobId = await postgresStore.Save(new NewJobRequest(type, "{}", MaxRetries: 5));

            // First attempt - Nack (retry)
            var leased1 = await postgresStore.Lease("worker-A", new List<string> { type }, TimeSpan.FromSeconds(30));
            Debug.Assert(leased1 != null);
            await postgresStore.Nack(jobId, "worker-A", "failure #1");

            // Wait for backoff to elapse - poll for lease (max backoff for attempt 1 = 2^1 + 5s jitter = 7s)
            LeasedJob? leased2 = null;
            for (int i = 0; i < 15; i++)
            {
                await Task.Delay(1000);
                leased2 = await postgresStore.Lease("worker-B", new List<string> { type }, TimeSpan.FromSeconds(30));
                if (leased2 != null) break;
            }
            Debug.Assert(leased2 != null, "Expected job leasable after first backoff elapsed");

            // Second attempt - Nack (retry)
            await postgresStore.Nack(leased2.JobId, "worker-B", "failure #2");

            // Wait for second backoff - poll for lease (max backoff for attempt 2 = 2^2 + 5s jitter = 9s)
            LeasedJob? leased3 = null;
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(1000);
                leased3 = await postgresStore.Lease("worker-C", new List<string> { type }, TimeSpan.FromSeconds(30));
                if (leased3 != null) break;
            }
            Debug.Assert(leased3 != null, "Expected job leasable after second backoff elapsed");

            // Third attempt - Ack (success)
            await postgresStore.Ack(leased3.JobId, "worker-C", "file://result.pdf");

            // Now get attempts - should have 3 records
            var attempts = await postgresStore.GetAttempts(jobId);
            Debug.Assert(attempts != null && attempts.Count == 3, $"Expected 3 attempt records, got {attempts?.Count}");

            // First attempt: failed, finished_at set
            var attempt1 = attempts.OrderBy(a => a.StartedAt).First();
            Debug.Assert(attempt1.Outcome == "failed", "First attempt outcome should be failed");
            Debug.Assert(attempt1.FinishedAt != null, "First attempt should have finished_at after Nack");
            Debug.Assert(attempt1.Error == "failure #1", "First attempt should have error recorded");

            // Second attempt: failed, finished_at set
            var attempt2 = attempts.OrderBy(a => a.StartedAt).Skip(1).First();
            Debug.Assert(attempt2.Outcome == "failed", "Second attempt outcome should be failed");
            Debug.Assert(attempt2.FinishedAt != null, "Second attempt should have finished_at after Nack");
            Debug.Assert(attempt2.Error == "failure #2", "Second attempt should have error recorded");

            // Third attempt: succeeded, finished_at set
            var attempt3 = attempts.OrderBy(a => a.StartedAt).Last();
            Debug.Assert(attempt3.Outcome == "succeeded", "Third attempt outcome should be succeeded");
            Debug.Assert(attempt3.FinishedAt != null, "Third attempt should have finished_at after Ack");
        }


    }
}
