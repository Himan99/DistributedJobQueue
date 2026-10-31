export interface WorkerInfo {
  workerId: string;
  pool: 'fast' | 'batch' | 'unknown';
  allowedTypes: string[];
  poolSize: number;
  inFlightCount: number;
  lastHeartbeat?: string;
  status: 'healthy' | 'stopping' | 'stopped';
}

export interface AdminActionResponse {
  jobId?: string;
  workerId?: string;
  status: string;
}