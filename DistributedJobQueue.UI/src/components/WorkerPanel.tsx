import { WorkerInfo } from '../types/worker';
import { Card, CardContent, CardHeader, CardTitle } from './ui/Card';
import { Badge } from './ui/Badge';
import { Button } from './ui/Button';
import { getPoolColor } from '../utils/format';
import { useWorkerShutdown, useWorkerStopHeartbeat } from '../api/workers';
import { AlertTriangle, Power } from 'lucide-react';

interface WorkerPanelProps {
  workers: WorkerInfo[];
}

export const WorkerPanel = ({ workers }: WorkerPanelProps) => {
  const shutdownWorker = useWorkerShutdown();
  const stopHeartbeatWorker = useWorkerStopHeartbeat();

  const handleShutdown = async (workerId: string) => {
    if (!confirm(`Shutdown worker ${workerId}?`)) return;
    try {
      await shutdownWorker.mutateAsync(workerId);
    } catch (error) {
      alert(`Failed to shutdown: ${error}`);
    }
  };

  const handleStopHeartbeat = async (workerId: string) => {
    if (!confirm(`Stop heartbeats for worker ${workerId}? This will simulate a crash.`)) return;
    try {
      await stopHeartbeatWorker.mutateAsync(workerId);
    } catch (error) {
      alert(`Failed to stop heartbeats: ${error}`);
    }
  };

  if (workers.length === 0) {
    return (
      <Card>
        <CardContent className="py-8 text-center text-muted-foreground">
          No workers found
        </CardContent>
      </Card>
    );
  }

  return (
    <div className="space-y-4">
      <h2 className="text-xl font-semibold">Worker Pool Status</h2>
      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
        {workers.map(worker => (
          <Card key={worker.workerId} className={worker.status === 'stopping' ? 'border-orange-500' : ''}>
            <CardHeader className="flex flex-row items-center justify-between">
              <CardTitle className="text-lg">
                <span className="font-mono text-sm">{worker.workerId}</span>
              </CardTitle>
              <Badge variant={worker.pool === 'fast' ? 'default' : 'secondary'} className={getPoolColor(worker.pool)}>
                {worker.pool}
              </Badge>
            </CardHeader>
            <CardContent className="space-y-3">
              <div className="grid grid-cols-2 gap-2 text-sm">
                <div>
                  <span className="text-muted-foreground">Pool Size</span>
                  <div className="font-medium">{worker.poolSize}</div>
                </div>
                <div>
                  <span className="text-muted-foreground">In Flight</span>
                  <div className="font-medium text-blue-600">{worker.inFlightCount}</div>
                </div>
                <div>
                  <span className="text-muted-foreground">Types</span>
                  <div className="font-medium">{worker.allowedTypes.join(', ')}</div>
                </div>
                <div>
                  <span className="text-muted-foreground">Last Heartbeat</span>
                  <div className="font-medium">
                    {worker.lastHeartbeat ? new Date(worker.lastHeartbeat).toLocaleTimeString() : 'N/A'}
                  </div>
                </div>
              </div>

              <div className="flex items-center gap-2 pt-2 border-t">
                <Badge variant={worker.status === 'healthy' ? 'success' : 'destructive'}>
                  {worker.status}
                </Badge>
                {worker.status === 'healthy' && worker.inFlightCount > 0 && (
                  <>
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => handleShutdown(worker.workerId)}
                      disabled={shutdownWorker.isPending}
                    >
                      <Power className="h-3 w-3 mr-1" />
                      Shutdown
                    </Button>
                    <Button
                      variant="destructive"
                      size="sm"
                      onClick={() => handleStopHeartbeat(worker.workerId)}
                      disabled={stopHeartbeatWorker.isPending}
                    >
                      <AlertTriangle className="h-3 w-3 mr-1" />
                      Stop Heartbeats
                    </Button>
                  </>
                )}
              </div>
            </CardContent>
          </Card>
        ))}
      </div>
    </div>
  );
};