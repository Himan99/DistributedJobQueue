import { useMemo } from 'react';
import { useJobs } from '../api/jobs';

export interface UseAdaptivePollingResult {
  refetchInterval: number | false;
  hasRunningJobs: boolean;
  hasQueuedJobs: boolean;
}

export const useAdaptivePolling = (): UseAdaptivePollingResult => {
  const { data: jobs = [] } = useJobs();

  const hasRunningJobs = useMemo(
    () => jobs.some(job => job.status === 'Running'),
    [jobs]
  );

  const hasQueuedJobs = useMemo(
    () => jobs.some(job => job.status === 'Queued'),
    [jobs]
  );

  const refetchInterval = useMemo(() => {
    if (hasRunningJobs) return 500; // Fast polling when jobs are running
    if (hasQueuedJobs) return 2000; // Medium polling when jobs are queued
    return 5000; // Slow polling when idle
  }, [hasRunningJobs, hasQueuedJobs]);

  return {
    refetchInterval,
    hasRunningJobs,
    hasQueuedJobs,
  };
};