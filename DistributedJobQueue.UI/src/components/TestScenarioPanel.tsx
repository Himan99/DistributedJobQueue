import { TestScenario } from '../utils/testScenarios';
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from './ui/Card';
import { Button } from './ui/Button';
import { Play, Loader2 } from 'lucide-react';
import { useBatchCreateJobs } from '../api/jobs';

interface TestScenarioPanelProps {
  scenarios: TestScenario[];
  onRunScenario: (scenario: TestScenario) => void;
  runningScenarioId?: string;
}

export const TestScenarioPanel = ({
  scenarios,
  onRunScenario,
  runningScenarioId
}: TestScenarioPanelProps) => {
  const batchCreateJobs = useBatchCreateJobs();

  const handleRun = async (scenario: TestScenario) => {
    try {
      await batchCreateJobs.mutateAsync(scenario.jobs);
      onRunScenario(scenario);
    } catch (error) {
      alert(`Failed to dispatch jobs: ${error}`);
    }
  };

  return (
    <div className="space-y-4">
      <h2 className="text-xl font-semibold">Test Scenarios</h2>
      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
        {scenarios.map(scenario => {
          const isRunning = runningScenarioId === scenario.id;
          return (
            <Card key={scenario.id} className={isRunning ? 'border-blue-500' : ''}>
              <CardHeader>
                <CardTitle className="flex items-center justify-between">
                  {scenario.name}
                  {isRunning && (
                    <span className="flex items-center gap-1 text-blue-600">
                      <Loader2 className="h-4 w-4 animate-spin" />
                      Running
                    </span>
                  )}
                </CardTitle>
                <CardDescription>{scenario.description}</CardDescription>
              </CardHeader>
              <CardContent className="space-y-3">
                <div className="text-sm text-muted-foreground">
                  {scenario.jobs.length} jobs · {scenario.expectedOutcome}
                </div>
                <div className="flex gap-2">
                  <Button
                    onClick={() => handleRun(scenario)}
                    disabled={isRunning || batchCreateJobs.isPending}
                    className="flex-1"
                  >
                    {isRunning ? (
                      <>
                        <Loader2 className="h-4 w-4 mr-2 animate-spin" />
                        Running...
                      </>
                    ) : (
                      <>
                        <Play className="h-4 w-4 mr-2" />
                        Run Test
                      </>
                    )}
                  </Button>
                </div>
              </CardContent>
            </Card>
          );
        })}
      </div>
    </div>
  );
};