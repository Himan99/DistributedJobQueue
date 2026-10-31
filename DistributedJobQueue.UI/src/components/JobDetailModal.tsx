import type { Job } from '../types/job';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from './ui/Dialog';
import { Button } from './ui/Button';
import { Badge } from './ui/Badge';
import { formatDateTime, formatRelativeTime, formatJson } from '../utils/format';
import { useReplayJob } from '../api/jobs';
import { X, Copy, ExternalLink, RotateCcw } from 'lucide-react';
import { useState } from 'react';

interface JobDetailModalProps {
  job: Job | null;
  onClose: () => void;
  onReplay?: () => void;
}

export const JobDetailModal = ({ job, onClose, onReplay }: JobDetailModalProps) => {
  const [copied, setCopied] = useState(false);
  const replayJob = useReplayJob();

  if (!job) return null;

  const handleCopy = (text: string) => {
    navigator.clipboard.writeText(text);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  const handleReplay = async () => {
    if (!confirm(`Replay job ${job.jobId}?`)) return;
    try {
      await replayJob.mutateAsync(job.jobId);
      onClose();
      onReplay?.();
    } catch (error) {
      alert(`Failed to replay: ${error}`);
    }
  };

  return (
    <Dialog defaultOpen>
      <DialogContent className="max-w-4xl max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <div className="flex items-start justify-between">
            <div>
              <DialogTitle className="flex items-center gap-2">
                <span className="font-mono text-sm text-muted-foreground">{job.jobId}</span>
                <Badge variant={job.status === 'Succeeded' ? 'success' : job.status === 'DeadLettered' ? 'destructive' : 'default'}>
                  {job.status}
                </Badge>
              </DialogTitle>
              <DialogDescription>Job type: {job.type}</DialogDescription>
            </div>
            <Button variant="ghost" size="icon" onClick={onClose}>
              <X className="h-4 w-4" />
            </Button>
          </div>
        </DialogHeader>

        <div className="space-y-6">
          {/* Basic Info */}
          <div className="grid gap-4 md:grid-cols-2">
            <div>
              <h4 className="font-medium mb-2">Basic Information</h4>
              <dl className="space-y-2 text-sm">
                <div className="grid grid-cols-[auto_1fr] gap-2">
                  <dt className="text-muted-foreground">Priority</dt>
                  <dd>{job.priority}</dd>
                  <dt className="text-muted-foreground">Max Retries</dt>
                  <dd>{job.maxRetries}</dd>
                  <dt className="text-muted-foreground">Attempts</dt>
                  <dd>{job.attempts}/{job.maxRetries}</dd>
                  <dt className="text-muted-foreground">Worker</dt>
                  <dd className="font-mono">{job.leasedBy || 'Not assigned'}</dd>
                  <dt className="text-muted-foreground">Created</dt>
                  <dd>{formatDateTime(job.createdAt)} ({formatRelativeTime(job.createdAt)})</dd>
                  <dt className="text-muted-foreground">Finished</dt>
                  <dd>{job.finishedAt ? formatDateTime(job.finishedAt) : 'Not finished'}</dd>
                  <dt className="text-muted-foreground">Lease Expires</dt>
                  <dd>{job.leaseExpiresAt ? formatRelativeTime(job.leaseExpiresAt) : 'N/A'}</dd>
                  <dt className="text-muted-foreground">Idempotency Key</dt>
                  <dd className="font-mono text-xs">{job.idempotencyKey || 'None'}</dd>
                </div>
              </dl>
            </div>

            <div>
              <h4 className="font-medium mb-2">Error & Result</h4>
              <dl className="space-y-2 text-sm">
                <div className="grid grid-cols-[auto_1fr] gap-2">
                  {job.lastError && (
                    <>
                      <dt className="text-muted-foreground">Last Error</dt>
                      <dd className="text-red-600 font-mono text-xs break-all">{job.lastError}</dd>
                    </>
                  )}
                  {job.resultUrl && (
                    <>
                      <dt className="text-muted-foreground">Result URL</dt>
                      <dd className="flex items-center gap-2">
                        <span className="font-mono text-xs break-all">{job.resultUrl}</span>
                        <Button variant="ghost" size="icon" onClick={() => handleCopy(job.resultUrl!)}>
                          <Copy className="h-4 w-4" />
                        </Button>
                        <a href={job.resultUrl} target="_blank" rel="noopener noreferrer">
                          <ExternalLink className="h-4 w-4 text-muted-foreground hover:text-foreground" />
                        </a>
                      </dd>
                    </>
                  )}
                </div>
              </dl>
            </div>
          </div>

          {/* Payload */}
          <div>
            <div className="flex items-center justify-between mb-2">
              <h4 className="font-medium">Payload</h4>
              <Button variant="ghost" size="sm" onClick={() => handleCopy(job.payload)}>
                <Copy className="h-4 w-4 mr-1" />
                {copied ? 'Copied!' : 'Copy'}
              </Button>
            </div>
            <pre className="bg-gray-100 dark:bg-gray-800 rounded p-4 text-sm overflow-x-auto max-h-64 overflow-y-auto font-mono">
              {formatJson(job.payload)}
            </pre>
          </div>
        </div>

        <DialogFooter>
          {job.status === 'DeadLettered' && (
            <Button
              variant="default"
              onClick={handleReplay}
              disabled={replayJob.isPending}
            >
              <RotateCcw className="h-4 w-4 mr-2" />
              Replay Job
            </Button>
          )}
          <Button variant="outline" onClick={onClose}>
            Close
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};