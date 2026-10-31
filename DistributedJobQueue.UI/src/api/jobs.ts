import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import type { Job, JobAttempt, NewJobRequest, JobStatus } from '../types/job';

const JOBS_KEY = 'jobs';
const JOB_KEY = 'job';
const ATTEMPTS_KEY = 'attempts';

export const useJobs = (status?: JobStatus, type?: string) => {
  return useQuery({
    queryKey: [JOBS_KEY, status, type],
    queryFn: async () => {
      const params = new URLSearchParams();
      if (status) params.append('status', status);
      if (type) params.append('type', type);
      const response = await apiClient.get<Job[]>(`/api/jobs?${params.toString()}`);
      return response.data;
    },
    refetchInterval: false, // Will be controlled by adaptive polling hook
  });
};

export const useJob = (jobId: string) => {
  return useQuery({
    queryKey: [JOB_KEY, jobId],
    queryFn: async () => {
      const response = await apiClient.get<Job>(`/api/jobs/${jobId}`);
      return response.data;
    },
    enabled: !!jobId,
    refetchInterval: false,
  });
};

export const useCreateJob = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (request: NewJobRequest) => {
      const response = await apiClient.post<string>('/api/jobs/save', request);
      return response.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [JOBS_KEY] });
    },
  });
};

export const useBatchCreateJobs = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (jobs: NewJobRequest[]) => {
      const results = await Promise.all(
        jobs.map(job => apiClient.post<string>('/api/jobs/save', job))
      );
      return results.map(r => r.data);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [JOBS_KEY] });
    },
  });
};

export const useAbortJob = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ jobId, reason }: { jobId: string; reason?: string }) => {
      const response = await apiClient.post<string>(`/api/jobs/${jobId}/abort`, { reason });
      return response.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [JOBS_KEY] });
      queryClient.invalidateQueries({ queryKey: [JOB_KEY] });
    },
  });
};

export const useSimulateCrash = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (jobId: string) => {
      const response = await apiClient.post<string>(`/api/jobs/${jobId}/simulate-crash`);
      return response.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [JOBS_KEY] });
      queryClient.invalidateQueries({ queryKey: [JOB_KEY] });
    },
  });
};

export const useReplayJob = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (jobId: string) => {
      const response = await apiClient.post<string>(`/api/jobs/${jobId}/replay`);
      return response.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [JOBS_KEY] });
      queryClient.invalidateQueries({ queryKey: [JOB_KEY] });
    },
  });
};

export const useJobAttempts = (jobId: string) => {
  return useQuery({
    queryKey: [ATTEMPTS_KEY, jobId],
    queryFn: async () => {
      const response = await apiClient.get<JobAttempt[]>(`/api/jobs/${jobId}/attempts`);
      return response.data;
    },
    enabled: !!jobId,
    refetchInterval: false,
  });
};