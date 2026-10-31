import { JobAttempt } from '../types/job';
import { Table, TableHeader, TableBody, TableRow, TableHead, TableCell } from './ui/Table';
import { Badge } from './ui/Badge';
import { formatDateTime, formatDuration } from '../utils/format';

interface AttemptHistoryProps {
  attempts: JobAttempt[];
}

export const AttemptHistory = ({ attempts }: AttemptHistoryProps) => {
  if (attempts.length === 0) {
    return (
      <div className="text-center py-8 text-muted-foreground">
        No attempt history available
      </div>
    );
  }

  const getOutcomeColor = (outcome: string) => {
    switch (outcome) {
      case 'Acked': return 'success';
      case 'Nacked': return 'destructive';
      case 'Failed': return 'destructive';
      case 'TimedOut': return 'secondary';
      default: return 'default';
    }
  };

  return (
    <div>
      <h4 className="font-medium mb-3">Attempt History ({attempts.length})</h4>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Attempt</TableHead>
            <TableHead>Worker</TableHead>
            <TableHead>Outcome</TableHead>
            <TableHead>Error</TableHead>
            <TableHead>Started</TableHead>
            <TableHead>Finished</TableHead>
            <TableHead>Duration</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {attempts.map(attempt => (
            <TableRow key={attempt.id}>
              <TableCell className="font-mono">{attempt.attemptNum}</TableCell>
              <TableCell className="font-mono text-sm">{attempt.workerId}</TableCell>
              <TableCell>
                <Badge variant={getOutcomeColor(attempt.outcome)}>{attempt.outcome}</Badge>
              </TableCell>
              <TableCell className="text-red-600 text-sm max-w-[200px] truncate">
                {attempt.error || '-'}
              </TableCell>
              <TableCell className="text-xs">{formatDateTime(attempt.startedAt)}</TableCell>
              <TableCell className="text-xs">
                {attempt.finishedAt ? formatDateTime(attempt.finishedAt) : '-'}
              </TableCell>
              <TableCell className="font-mono text-xs">
                {attempt.finishedAt ? formatDuration(attempt.startedAt, attempt.finishedAt) : '-'}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
};