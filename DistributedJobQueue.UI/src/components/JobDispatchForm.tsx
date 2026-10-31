import { useState } from 'react';
import { NewJobRequest } from '../types/job';
import { Card, CardContent, CardHeader, CardTitle } from './ui/Card';
import { Button } from './ui/Button';
import { Input } from './ui/Input';
import { Select, SelectOption } from './ui/Select';
import { useBatchCreateJobs } from '../api/jobs';

interface JobDispatchFormProps {
  onDispatch: (jobs: NewJobRequest[]) => void;
}

export const JobDispatchForm = ({ onDispatch }: JobDispatchFormProps) => {
  const [type, setType] = useState('email');
  const [payload, setPayload] = useState('{}');
  const [count, setCount] = useState(10);
  const [maxRetries, setMaxRetries] = useState(3);
  const [priority, setPriority] = useState(0);
  const [idempotencyKey, setIdempotencyKey] = useState('');
  const [runAfter, setRunAfter] = useState(0);

  const batchCreateJobs = useBatchCreateJobs();

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();

    try {
      JSON.parse(payload);
    } catch {
      alert('Invalid JSON payload');
      return;
    }

    const jobs: NewJobRequest[] = Array.from({ length: count }, (_, i) => ({
      type,
      payload: payload.replace(/\{id\}/g, String(i + 1)),
      maxRetries,
      priority,
      idempotencyKey: idempotencyKey ? `${idempotencyKey}-${i + 1}` : undefined,
      runAfter: runAfter > 0 ? `${runAfter}s` : undefined,
    }));

    onDispatch(jobs);
  };

  const handlePayloadChange = (e: React.ChangeEvent<HTMLTextAreaElement>) => {
    setPayload(e.target.value);
  };

  const jobTypes = [
    { value: 'email', label: 'Email (Fast Pool)' },
    { value: 'sms', label: 'SMS (Fast Pool)' },
    { value: 'report', label: 'Report (Batch Pool)' },
    { value: 'ach', label: 'ACH (Batch Pool)' },
  ];

  return (
    <Card>
      <CardHeader>
        <CardTitle>Dispatch Jobs</CardTitle>
      </CardHeader>
      <CardContent>
        <form onSubmit={handleSubmit} className="space-y-4">
          <div className="grid gap-4 md:grid-cols-2">
            <div>
              <label className="block text-sm font-medium mb-1">Job Type</label>
              <Select value={type} onValueChange={setType}>
                {jobTypes.map(t => (
                  <SelectOption key={t.value} value={t.value}>{t.label}</SelectOption>
                ))}
              </Select>
            </div>
            <div>
              <label className="block text-sm font-medium mb-1">Count</label>
              <Input
                type="number"
                value={count}
                onChange={e => setCount(Math.max(1, Math.min(1000, parseInt(e.target.value) || 1)))}
                min={1}
                max={1000}
              />
            </div>
            <div>
              <label className="block text-sm font-medium mb-1">Max Retries</label>
              <Input
                type="number"
                value={maxRetries}
                onChange={e => setMaxRetries(Math.max(0, Math.min(10, parseInt(e.target.value) || 0)))}
                min={0}
                max={10}
              />
            </div>
            <div>
              <label className="block text-sm font-medium mb-1">Priority</label>
              <Input
                type="number"
                value={priority}
                onChange={e => setPriority(Math.max(-100, Math.min(100, parseInt(e.target.value) || 0)))}
                min={-100}
                max={100}
              />
            </div>
          </div>

          <div>
            <label className="block text-sm font-medium mb-1">Idempotency Key Prefix (optional)</label>
            <Input
              value={idempotencyKey}
              onChange={e => setIdempotencyKey(e.target.value)}
              placeholder="e.g., order-123 (will append -1, -2, etc.)"
            />
          </div>

          <div>
            <label className="block text-sm font-medium mb-1">Run After Delay (seconds, optional)</label>
            <Input
              type="number"
              value={runAfter}
              onChange={e => setRunAfter(Math.max(0, parseInt(e.target.value) || 0))}
              min={0}
              placeholder="0 = immediate"
            />
          </div>

          <div>
            <label className="block text-sm font-medium mb-1">Payload (JSON)</label>
            <textarea
              value={payload}
              onChange={handlePayloadChange}
              rows={6}
              className="w-full font-mono text-sm p-3 border rounded-md bg-gray-50 dark:bg-gray-800"
              placeholder='{"message": "Hello", "to": "user@example.com", "id": "\{id\}"}'
            />
            <p className="text-xs text-muted-foreground mt-1">
              Use &#123;id&#125; placeholder for unique values per job (e.g., &#123;id&#125; → 1, 2, 3...)
            </p>
          </div>

          <div className="flex gap-2 pt-4">
            <Button type="submit" disabled={batchCreateJobs.isPending} className="flex-1">
              {batchCreateJobs.isPending ? 'Dispatching...' : `Dispatch ${count} Jobs`}
            </Button>
            <Button type="button" variant="outline" onClick={() => setPayload('{}')}>
              Clear Payload
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  );
};