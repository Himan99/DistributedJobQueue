import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import type { WorkerInfo, AdminActionResponse } from '../types/worker';

const WORKERS_KEY = 'workers';
const WORKER_API_BASE = '/admin';

export const useWorkers = () => {
  return useQuery({
    queryKey: [WORKERS_KEY],
    queryFn: async () => {
      const response = await apiClient.get<WorkerInfo[]>(`${WORKER_API_BASE}/workers`);
      return response.data;
    },
    refetchInterval: false,
  });
};

export const useWorkerShutdown = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (workerId: string) => {
      const response = await apiClient.post<AdminActionResponse>(`${WORKER_API_BASE}/workers/${workerId}/shutdown`);
      return response.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [WORKERS_KEY] });
    },
  });
};

export const useWorkerStopHeartbeat = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (workerId: string) => {
      const response = await apiClient.post<AdminActionResponse>(`${WORKER_API_BASE}/workers/${workerId}/stop-heartbeat`);
      return response.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [WORKERS_KEY] });
    },
  });
};

export const useAbortJobWorker = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ jobId, reason }: { jobId: string; reason?: string }) => {
      const response = await apiClient.post<AdminActionResponse>(`${WORKER_API_BASE}/jobs/${jobId}/abort`, { reason });
      return response.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [WORKERS_KEY] });
    },
  });
};

export const useSimulateCrashWorker = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (jobId: string) => {
      const response = await apiClient.post<AdminActionResponse>(`${WORKER_API_BASE}/jobs/${jobId}/simulate-crash`);
      return response.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [WORKERS_KEY] });
    },
  });
};