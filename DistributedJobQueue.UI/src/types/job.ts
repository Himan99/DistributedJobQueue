export type JobStatus = 'Queued' | 'Running' | 'Succeeded' | 'DeadLettered';

export interface Job {
  jobId: string;
  type: string;
  payload: string;
  status: JobStatus;
  attempts: number;
  maxRetries: number;
  priority: number;
  idempotencyKey?: string;
  leasedBy?: string;
  leaseExpiresAt?: string;
  runAfter?: string;
  resultUrl?: string;
  lastError?: string;
  createdAt: string;
  finishedAt?: string;
}

export interface JobAttempt {
  id: string;
  jobId: string;
  attemptNum: number;
  workerId: string;
  outcome: 'Acked' | 'Nacked' | 'Failed' | 'TimedOut';
  error?: string;
  startedAt: string;
  finishedAt?: string;
}

export interface NewJobRequest {
  type: string;
  payload: string;
  maxRetries?: number;
  idempotencyKey?: string;
  runAfter?: string;
  priority?: number;
}

export interface LeaseRequest {
  workerId: string;
  allowedTypes?: string[];
  leaseSeconds?: number;
}

export interface AckRequest {
  jobId: string;
  workerId: string;
  resultUrl: string;
}

export interface NackRequest {
  jobId: string;
  workerId: string;
  error: string;
}

export interface HeartbeatRequest {
  jobId: string;
  workerId: string;
  leaseExtensionSeconds?: number;
}

export interface AbortRequest {
  reason?: string;
}