export interface MetricPoint {
  timestamp: number;
  value: number;
}

export interface MetricSeries {
  label: string;
  data: MetricPoint[];
}

export interface DashboardMetrics {
  queuedByType: Record<string, number>;
  runningByType: Record<string, number>;
  succeededTotal: number;
  deadLetteredTotal: number;
  throughputHistory: MetricPoint[];
  latencyHistory: MetricPoint[];
  queueDepthHistory: MetricPoint[];
  retryRateHistory: MetricPoint[];
  workerInFlight: Record<string, number>;
}