import { Job } from '../types/job';
import { Card, CardContent, CardHeader, CardTitle } from './ui/Card';

interface StatusCardsProps {
  jobs: Job[];
}

export const StatusCards = ({ jobs }: StatusCardsProps) => {
  const counts = {
    Queued: jobs.filter(j => j.status === 'Queued').length,
    Running: jobs.filter(j => j.status === 'Running').length,
    Succeeded: jobs.filter(j => j.status === 'Succeeded').length,
    DeadLettered: jobs.filter(j => j.status === 'DeadLettered').length,
  };

  const statuses = [
    { key: 'Queued', label: 'Queued', color: 'bg-gray-500' },
    { key: 'Running', label: 'Running', color: 'bg-blue-500' },
    { key: 'Succeeded', label: 'Succeeded', color: 'bg-green-500' },
    { key: 'DeadLettered', label: 'Dead Lettered', color: 'bg-red-500' },
  ] as const;

  return (
    <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
      {statuses.map(({ key, label, color }) => (
        <Card key={key}>
          <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
            <CardTitle className="text-sm font-medium">{label}</CardTitle>
            <div className={`h-2.5 w-2.5 rounded-full ${color}`} />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold">{counts[key as keyof typeof counts]}</div>
          </CardContent>
        </Card>
      ))}
    </div>
  );
};