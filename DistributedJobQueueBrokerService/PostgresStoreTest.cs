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
        public PostgresStoreTest(IJobStore jobStore) 
        {
            postgresStore = jobStore;
            random = new Random();
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

            return;
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

            for (int i = 0; i < 2; i++)
            {
                // each loop: lease will fail if run_after backoff hasn't elapsed — see note above,
                // this test assumes a short/no backoff for test purposes, or direct row manipulation.
                var leased = await postgresStore.Lease($"worker-{i}", new List<string> { type }, TimeSpan.FromSeconds(30));
                Debug.Assert(leased != null, $"Expected job leasable on loop {i}");
                await postgresStore.Nack(jobId, $"worker-{i}", $"failure #{i}");
                await Task.Delay(1000);
            }

            var finalAttempt = await postgresStore.Lease("worker-final", new List<string> { type }, TimeSpan.FromSeconds(30));
            Debug.Assert(finalAttempt == null, "Job was still leasable after exhausting max_retries — should be DeadLettered");
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


    }
}
