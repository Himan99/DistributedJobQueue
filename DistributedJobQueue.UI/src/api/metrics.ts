import { useQuery } from '@tanstack/react-query';
import { apiClient } from './client';
import type { DashboardMetrics } from '../types/metrics';

const METRICS_KEY = 'metrics';

function parsePrometheusMetrics(text: string): DashboardMetrics {
  const lines = text.split('\n');
  const metrics: DashboardMetrics = {
    queuedByType: {},
    runningByType: {},
    succeededTotal: 0,
    deadLetteredTotal: 0,
    throughputHistory: [],
    latencyHistory: [],
    queueDepthHistory: [],
    retryRateHistory: [],
    workerInFlight: {},
  };

  for (const line of lines) {
    if (line.startsWith('#') || !line.trim()) continue;

    // Parse gauge: jobs_queued_total{type="email"} 5
    const queuedMatch = line.match(/jobs_queued_total\{type="([^"]+)"\}\s+(\d+(\.\d+)?)/);
    if (queuedMatch) {
      metrics.queuedByType[queuedMatch[1]] = parseFloat(queuedMatch[2]);
      continue;
    }

    // Parse gauge: jobs_running_total{type="email"} 3
    const runningMatch = line.match(/jobs_running_total\{type="([^"]+)"\}\s+(\d+(\.\d+)?)/);
    if (runningMatch) {
      metrics.runningByType[runningMatch[1]] = parseFloat(runningMatch[2]);
      continue;
    }

    // Parse counter: jobs_succeeded_total 100
    const succeededMatch = line.match(/jobs_succeeded_total\s+(\d+(\.\d+)?)/);
    if (succeededMatch) {
      metrics.succeededTotal = parseFloat(succeededMatch[1]);
      continue;
    }

    // Parse counter: jobs_dead_lettered_total 5
    const deadLetteredMatch = line.match(/jobs_dead_lettered_total\s+(\d+(\.\d+)?)/);
    if (deadLetteredMatch) {
      metrics.deadLetteredTotal = parseFloat(deadLetteredMatch[1]);
      continue;
    }

    // Parse histogram for lease duration (latency)
    const leaseDurationMatch = line.match(/job_lease_duration_seconds_bucket\{le="([^"]+)",outcome="([^"]+)"\}\s+(\d+(\.\d+)?)/);
    if (leaseDurationMatch) {
      metrics.latencyHistory.push({
        timestamp: Date.now(),
        value: parseFloat(leaseDurationMatch[1]),
      });
      continue;
    }

    // Parse gauge: worker_in_flight{pool="worker-1"} 3
    const workerInFlightMatch = line.match(/worker_in_flight\{pool="([^"]+)"\}\s+(\d+(\.\d+)?)/);
    if (workerInFlightMatch) {
      metrics.workerInFlight[workerInFlightMatch[1]] = parseFloat(workerInFlightMatch[2]);
      continue;
    }
  }

  return metrics;
}

export const useMetrics = () => {
  return useQuery({
    queryKey: [METRICS_KEY],
    queryFn: async () => {
      const response = await apiClient.get<string>('/metrics', {
        headers: { Accept: 'text/plain' },
      });
      return parsePrometheusMetrics(response.data);
    },
    refetchInterval: false,
  });
};