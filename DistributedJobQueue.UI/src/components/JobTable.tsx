import { useMemo, useState } from 'react';
import { Job, JobStatus } from '../types/job';
import { Table, TableHeader, TableBody, TableRow, TableHead, TableCell } from './ui/Table';
import { Badge } from './ui/Badge';
import { DropdownMenu, DropdownMenuTrigger, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuLabel } from './ui/DropdownMenu';
import { Button } from './ui/Button';
import { formatDateTime, formatRelativeTime } from '../utils/format';
import { useAbortJob } from '../api/jobs';
import { useSimulateCrashWorker } from '../api/workers';
import { useReplayJob } from '../api/jobs';
import { MoreHorizontal, RotateCcw, XCircle, AlertTriangle, Table as TableIcon, Layout } from 'lucide-react';

type ViewMode = 'table' | 'treemap';

interface JobTableProps {
  jobs: Job[];
  onJobClick?: (job: Job) => void;
}

export const JobTable = ({ jobs, onJobClick }: JobTableProps) => {
  const [sortConfig, setSortConfig] = useState<{ key: string; direction: 'asc' | 'desc' }>({ key: 'createdAt', direction: 'desc' });
  const [filterStatus, setFilterStatus] = useState<JobStatus | 'All'>('All');
  const [filterType, setFilterType] = useState<string>('');
  const [viewMode, setViewMode] = useState<ViewMode>('table');

  const abortJob = useAbortJob();
  const simulateCrashWorker = useSimulateCrashWorker();
  const replayJob = useReplayJob();

  const filteredJobs = useMemo(() => {
    return jobs
      .filter(job => filterStatus === 'All' || job.status === filterStatus)
      .filter(job => !filterType || job.type.includes(filterType))
      .sort((a, b) => {
        let aVal: any = a[sortConfig.key as keyof Job];
        let bVal: any = b[sortConfig.key as keyof Job];

        if (aVal === undefined) aVal = '';
        if (bVal === undefined) bVal = '';

        if (typeof aVal === 'string') aVal = aVal.toLowerCase();
        if (typeof bVal === 'string') bVal = bVal.toLowerCase();

        if (aVal < bVal) return sortConfig.direction === 'asc' ? -1 : 1;
        if (aVal > bVal) return sortConfig.direction === 'asc' ? 1 : -1;
        return 0;
      });
  }, [jobs, filterStatus, filterType, sortConfig]);

  const handleSort = (key: string) => {
    setSortConfig(current => ({
      key,
      direction: current.key === key && current.direction === 'asc' ? 'desc' : 'asc',
    }));
  };

  const handleAbort = async (job: Job) => {
    if (!confirm(`Force abort job ${job.jobId}?`)) return;
    try {
      await abortJob.mutateAsync({ jobId: job.jobId, reason: 'Force aborted via UI' });
    } catch (error) {
      alert(`Failed to abort: ${error}`);
    }
  };

  const handleSimulateCrash = async (job: Job) => {
    if (!confirm(`Simulate crash for job ${job.jobId}?`)) return;
    try {
      await simulateCrashWorker.mutateAsync(job.jobId);
    } catch (error) {
      alert(`Failed to simulate crash: ${error}`);
    }
  };

  const handleReplay = async (job: Job) => {
    if (!confirm(`Replay job ${job.jobId}?`)) return;
    try {
      await replayJob.mutateAsync(job.jobId);
    } catch (error) {
      alert(`Failed to replay: ${error}`);
    }
  };

  const getActionItems = (job: Job) => {
    const items = [];

    if (job.status === 'Running') {
      items.push(
        <DropdownMenuItem
          key="abort"
          onClick={() => handleAbort(job)}
          className="text-red-600"
        >
          <XCircle className="h-4 w-4 mr-2" />
          Force Abort
        </DropdownMenuItem>
      );
      items.push(
        <DropdownMenuItem
          key="crash"
          onClick={() => handleSimulateCrash(job)}
          className="text-orange-600"
        >
          <AlertTriangle className="h-4 w-4 mr-2" />
          Simulate Crash
        </DropdownMenuItem>
      );
      items.push(<DropdownMenuSeparator key="sep1" />);
    }

    if (job.status === 'DeadLettered') {
      items.push(
        <DropdownMenuItem
          key="replay"
          onClick={() => handleReplay(job)}
          className="text-green-600"
        >
          <RotateCcw className="h-4 w-4 mr-2" />
          Replay
        </DropdownMenuItem>
      );
    }

    return items;
  };

  // Treemap data preparation
  const treemapData = useMemo(() => {
    const grouped: Record<string, Record<string, number>> = {};

    filteredJobs.forEach(job => {
      if (!grouped[job.type]) {
        grouped[job.type] = {};
      }
      grouped[job.type][job.status] = (grouped[job.type][job.status] || 0) + 1;
    });

    return Object.entries(grouped).flatMap(([type, statuses]) =>
      Object.entries(statuses).map(([status, count]) => ({
        type,
        status,
        count,
      }))
    );
  }, [filteredJobs]);

  const statusColors: Record<string, string> = {
    Queued: '#6b7280',      // gray-500
    Running: '#3b82f6',     // blue-500
    Succeeded: '#22c55e',   // green-500
    DeadLettered: '#ef4444', // red-500
  };

  return (
    <div className="space-y-4">
      {/* Toolbar */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
        <div className="flex gap-2">
          <select
            value={filterStatus}
            onChange={e => setFilterStatus(e.target.value as JobStatus | 'All')}
            className="px-3 py-2 border rounded-md text-sm"
          >
            <option value="All">All Statuses</option>
            <option value="Queued">Queued</option>
            <option value="Running">Running</option>
            <option value="Succeeded">Succeeded</option>
            <option value="DeadLettered">Dead Lettered</option>
          </select>
          <input
            type="text"
            placeholder="Filter by type..."
            value={filterType}
            onChange={e => setFilterType(e.target.value)}
            className="px-3 py-2 border rounded-md text-sm w-64"
          />
        </div>

        <div className="flex items-center gap-2">
          <span className="text-sm text-muted-foreground">View:</span>
          <Button
            variant={viewMode === 'table' ? 'default' : 'outline'}
            size="sm"
            onClick={() => setViewMode('table')}
          >
            <TableIcon className="h-4 w-4" />
          </Button>
          <Button
            variant={viewMode === 'treemap' ? 'default' : 'outline'}
            size="sm"
            onClick={() => setViewMode('treemap')}
          >
            <Layout className="h-4 w-4" />
          </Button>
        </div>
      </div>

      {viewMode === 'table' ? (
        <>
          <Table>
            <TableHeader>
              <TableRow>
                {[
                  { key: 'jobId', label: 'ID' },
                  { key: 'type', label: 'Type' },
                  { key: 'status', label: 'Status' },
                  { key: 'leasedBy', label: 'Worker' },
                  { key: 'attempts', label: 'Attempt' },
                  { key: 'priority', label: 'Priority' },
                  { key: 'leaseExpiresAt', label: 'Lease Expires' },
                  { key: 'createdAt', label: 'Created' },
                  { key: 'actions', label: 'Actions' },
                ].map(({ key, label }) => (
                  <TableHead
                    key={key}
                    className="cursor-pointer"
                    onClick={() => key !== 'actions' && handleSort(key)}
                  >
                    <div className="flex items-center gap-1">
                      {label}
                      {sortConfig.key === key && (
                        <span>{sortConfig.direction === 'asc' ? '↑' : '↓'}</span>
                      )}
                    </div>
                  </TableHead>
                ))}
              </TableRow>
            </TableHeader>
            <TableBody>
              {filteredJobs.length === 0 ? (
                <TableRow>
                  <TableCell colSpan={9} className="text-center py-8 text-muted-foreground">
                    No jobs found
                  </TableCell>
                </TableRow>
              ) : (
                filteredJobs.map(job => (
                  <TableRow key={job.jobId} onClick={() => onJobClick?.(job)} className="cursor-pointer">
                    <TableCell className="font-mono text-xs max-w-[120px] truncate">{job.jobId.slice(0, 8)}...</TableCell>
                    <TableCell><span className="font-medium">{job.type}</span></TableCell>
                    <TableCell>
                      <Badge variant={job.status === 'Succeeded' ? 'success' : job.status === 'DeadLettered' ? 'destructive' : 'default'}>
                        {job.status}
                      </Badge>
                    </TableCell>
                    <TableCell className="font-mono text-xs">{job.leasedBy || '-'}</TableCell>
                    <TableCell>{job.attempts}/{job.maxRetries}</TableCell>
                    <TableCell>{job.priority}</TableCell>
                    <TableCell className="text-xs">
                      {job.leaseExpiresAt ? formatRelativeTime(job.leaseExpiresAt) : '-'}
                    </TableCell>
                    <TableCell className="text-xs">{formatDateTime(job.createdAt)}</TableCell>
                    <TableCell>
                      <DropdownMenu>
                        <DropdownMenuTrigger asChild>
                          <Button variant="ghost" size="icon" className="h-8 w-8">
                            <MoreHorizontal className="h-4 w-4" />
                          </Button>
                        </DropdownMenuTrigger>
                        <DropdownMenuContent>
                          <DropdownMenuLabel>Actions</DropdownMenuLabel>
                          {getActionItems(job)}
                        </DropdownMenuContent>
                      </DropdownMenu>
                    </TableCell>
                  </TableRow>
                ))
              )}
            </TableBody>
          </Table>
        </>
      ) : (
        <>
          <div className="space-y-4">
            <div className="flex items-center justify-between">
              <h3 className="text-lg font-medium">Job Distribution by Type & Status</h3>
              <div className="flex gap-4 text-sm">
                {Object.entries(statusColors).map(([status, color]) => (
                  <span key={status} className="flex items-center gap-1.5">
                    <span className="w-3 h-3 rounded" style={{ backgroundColor: color }} />
                    {status}
                  </span>
                ))}
              </div>
            </div>
            <div className="bg-white dark:bg-card rounded-lg border p-4">
              {treemapData.length > 0 ? (
                <CustomTreemap
                  data={treemapData}
                  colors={statusColors}
                  height={384}
                />
              ) : (
                <div className="flex items-center justify-center h-96 text-muted-foreground">
                  No jobs to display
                </div>
              )}
            </div>
          </div>
        </>
      )}
    </div>
  );
};

// Simple treemap layout: group by type (columns), stack statuses vertically
interface TreemapNode {
  x: number;
  y: number;
  width: number;
  height: number;
  type: string;
  status: string;
  count: number;
}

interface CustomTreemapProps {
  data: { type: string; status: string; count: number }[];
  colors: Record<string, string>;
  height: number;
}

const CustomTreemap = ({ data, colors, height }: CustomTreemapProps) => {
  // Group by type
  const grouped = data.reduce((acc, item) => {
    if (!acc[item.type]) acc[item.type] = [];
    acc[item.type].push(item);
    return acc;
  }, {} as Record<string, typeof data>);

  const types = Object.keys(grouped);
  const typeCount = types.length;
  const containerWidth = 800; // Will be scaled by SVG viewBox

  // Calculate layout: each type gets a column, statuses stacked vertically
  const typeWidth = containerWidth / typeCount;

  const rectangles: TreemapNode[] = [];

  types.forEach((type, typeIndex) => {
    const statuses = grouped[type];
    const typeTotal = statuses.reduce((sum, s) => sum + s.count, 0);
    let y = 0;

    statuses.forEach(statusItem => {
      const rectHeight = typeTotal > 0 ? (statusItem.count / typeTotal) * height : 0;
      rectangles.push({
        x: typeIndex * typeWidth,
        y,
        width: typeWidth,
        height: rectHeight,
        type,
        status: statusItem.status,
        count: statusItem.count,
      });
      y += rectHeight;
    });
  });

  return (
    <svg
      viewBox={`0 0 ${containerWidth} ${height}`}
      className="w-full h-full"
      style={{ maxHeight: height, width: '100%' }}
    >
      {rectangles.map((rect, i) => (
        rect.height > 0 && rect.width > 0 && (
          <g key={i}>
            <rect
              x={rect.x}
              y={rect.y}
              width={rect.width}
              height={rect.height}
              fill={colors[rect.status] || '#888'}
              stroke="#fff"
              strokeWidth="1"
              rx={2}
              ry={2}
            />
            <text
              x={rect.x + rect.width / 2}
              y={rect.y + rect.height / 2}
              dominantBaseline="middle"
              textAnchor="middle"
              fontSize="11"
              fill="white"
              fontWeight="600"
              pointerEvents="none"
            >
              {rect.type}: {rect.status} ({rect.count})
            </text>
          </g>
        )
      ))}
    </svg>
  );
};

export { CustomTreemap };