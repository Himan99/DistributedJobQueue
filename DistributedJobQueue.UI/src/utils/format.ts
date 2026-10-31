import { format, formatDistanceToNow, parseISO } from 'date-fns';

export const formatDateTime = (dateString: string): string => {
  try {
    const date = parseISO(dateString);
    return format(date, 'yyyy-MM-dd HH:mm:ss');
  } catch {
    return dateString;
  }
};

export const formatRelativeTime = (dateString: string): string => {
  try {
    const date = parseISO(dateString);
    return formatDistanceToNow(date, { addSuffix: true });
  } catch {
    return dateString;
  }
};

export const formatDuration = (startedAt: string, finishedAt?: string): string => {
  try {
    const start = parseISO(startedAt);
    const end = finishedAt ? parseISO(finishedAt) : new Date();
    const diffMs = end.getTime() - start.getTime();
    const diffSec = Math.floor(diffMs / 1000);
    if (diffSec < 60) return `${diffSec}s`;
    const diffMin = Math.floor(diffSec / 60);
    if (diffMin < 60) return `${diffMin}m ${diffSec % 60}s`;
    const diffHour = Math.floor(diffMin / 60);
    return `${diffHour}h ${diffMin % 60}m`;
  } catch {
    return 'unknown';
  }
};

export const formatJson = (jsonString: string): string => {
  try {
    const parsed = JSON.parse(jsonString);
    return JSON.stringify(parsed, null, 2);
  } catch {
    return jsonString;
  }
};

export const getStatusColor = (status: string): string => {
  switch (status) {
    case 'Queued':
      return 'bg-gray-100 text-gray-800 dark:bg-gray-800 dark:text-gray-200';
    case 'Running':
      return 'bg-blue-100 text-blue-800 dark:bg-blue-900 dark:text-blue-200';
    case 'Succeeded':
      return 'bg-green-100 text-green-800 dark:bg-green-900 dark:text-green-200';
    case 'DeadLettered':
      return 'bg-red-100 text-red-800 dark:bg-red-900 dark:text-red-200';
    default:
      return 'bg-gray-100 text-gray-800 dark:bg-gray-800 dark:text-gray-200';
  }
};

export const getPoolColor = (pool: string): string => {
  switch (pool) {
    case 'fast':
      return 'bg-purple-100 text-purple-800 dark:bg-purple-900 dark:text-purple-200';
    case 'batch':
      return 'bg-orange-100 text-orange-800 dark:bg-orange-900 dark:text-orange-200';
    default:
      return 'bg-gray-100 text-gray-800 dark:bg-gray-800 dark:text-gray-200';
  }
};