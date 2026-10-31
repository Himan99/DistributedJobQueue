import type { NewJobRequest } from '../types/job';

export interface TestScenario {
  id: string;
  name: string;
  description: string;
  jobs: NewJobRequest[];
  expectedOutcome: string;
}

export const testScenarios: TestScenario[] = [
  {
    id: 'throughput',
    name: 'Basic Throughput',
    description: 'Fire N jobs of same type, measure completion rate',
    jobs: Array.from({ length: 100 }, (_, i) => ({
      type: 'email',
      payload: JSON.stringify({ message: `Test email ${i}`, to: `user${i}@example.com` }),
      priority: 0,
      maxRetries: 3,
    })),
    expectedOutcome: 'All 100 jobs processed successfully',
  },
  {
    id: 'retry-backoff',
    name: 'Retry/Backoff Behavior',
    description: 'Jobs that fail N times then succeed, or exhaust retries',
    jobs: [
      {
        type: 'email',
        payload: JSON.stringify({ message: 'Fail twice then succeed', failCount: 2 }),
        priority: 0,
        maxRetries: 5,
      },
      {
        type: 'email',
        payload: JSON.stringify({ message: 'Fail all retries', failCount: 10 }),
        priority: 0,
        maxRetries: 3,
      },
    ],
    expectedOutcome: 'First job succeeds after 2 retries, second job dead-lettered after 3 retries',
  },
  {
    id: 'lease-expiry',
    name: 'Lease Expiration',
    description: 'Jobs that take longer than lease, get re-leased to another worker',
    jobs: [
      {
        type: 'report',
        payload: JSON.stringify({ duration: 30000 }), // 30s job with 20s lease
        priority: 0,
        maxRetries: 3,
      },
    ],
    expectedOutcome: 'Job re-leased after lease expires, processed by another worker',
  },
  {
    id: 'heartbeat',
    name: 'Heartbeat Extension',
    description: 'Long-running jobs that send heartbeats',
    jobs: [
      {
        type: 'report',
        payload: JSON.stringify({ duration: 60000, heartbeatInterval: 5000 }),
        priority: 0,
        maxRetries: 3,
      },
    ],
    expectedOutcome: 'Job heartbeat extends lease, completes without re-lease',
  },
  {
    id: 'concurrent-lease',
    name: 'Concurrent Lease Safety',
    description: 'Multiple workers, verify no duplicate processing (SKIP LOCKED)',
    jobs: Array.from({ length: 20 }, (_, i) => ({
      type: 'email',
      payload: JSON.stringify({ id: i, data: 'concurrent test' }),
      priority: 0,
      maxRetries: 3,
    })),
    expectedOutcome: 'No duplicate processing with multiple workers',
  },
  {
    id: 'priority',
    name: 'Priority Ordering',
    description: 'High-priority jobs processed before low-priority',
    jobs: [
      { type: 'email', payload: JSON.stringify({ priority: 'low' }), priority: 0 },
      { type: 'email', payload: JSON.stringify({ priority: 'medium' }), priority: 5 },
      { type: 'email', payload: JSON.stringify({ priority: 'high' }), priority: 10 },
      { type: 'email', payload: JSON.stringify({ priority: 'critical' }), priority: 20 },
    ],
    expectedOutcome: 'Jobs processed in priority order: critical > high > medium > low',
  },
  {
    id: 'idempotency',
    name: 'Idempotency Key Dedupe',
    description: 'Duplicate submissions with same key are deduped',
    jobs: Array.from({ length: 5 }, () => ({
      type: 'sms',
      payload: JSON.stringify({ phone: '+1234567890', message: 'Dedupe test' }),
      idempotencyKey: 'same-key-for-all',
      priority: 0,
      maxRetries: 3,
    })),
    expectedOutcome: 'Only one job created, rest deduplicated',
  },
  {
    id: 'dlq',
    name: 'Dead Letter Queue',
    description: 'Jobs that fail all retries go to DLQ, can replay',
    jobs: [
      {
        type: 'email',
        payload: JSON.stringify({ alwaysFail: true }),
        priority: 0,
        maxRetries: 2,
      },
    ],
    expectedOutcome: 'Job dead-lettered after 2 retries, can be replayed',
  },
  {
    id: 'pool-isolation',
    name: 'Pool Isolation',
    description: 'Fast (email/sms) vs batch (report/ach) workers only process their types',
    jobs: [
      { type: 'email', payload: JSON.stringify({ test: 'fast pool' }), priority: 0 },
      { type: 'report', payload: JSON.stringify({ test: 'batch pool' }), priority: 0 },
      { type: 'ach', payload: JSON.stringify({ test: 'batch pool' }), priority: 0 },
      { type: 'sms', payload: JSON.stringify({ test: 'fast pool' }), priority: 0 },
    ],
    expectedOutcome: 'Fast workers process email/sms only, batch workers process report/ach only',
  },
  {
    id: 'graceful-shutdown',
    name: 'Graceful Shutdown',
    description: 'Kill worker mid-job, verify job completes or gets re-queued',
    jobs: [
      {
        type: 'report',
        payload: JSON.stringify({ duration: 5000 }),
        priority: 0,
        maxRetries: 3,
      },
    ],
    expectedOutcome: 'Worker shuts down gracefully, in-flight job completes or re-queued',
  },
];

export function getScenarioById(id: string): TestScenario | undefined {
  return testScenarios.find(s => s.id === id);
}