import { DashboardMetrics } from '../types/metrics';
import { Card, CardContent, CardHeader, CardTitle } from './ui/Card';
import { AreaChart, BarChart, DonutChart } from '@tremor/react';

interface MetricsChartsProps {
  metrics: DashboardMetrics;
}

// Tremor-compatible colors that work in both light and dark mode
// Using explicit hex values with good contrast in both themes
const CHART_COLORS = {
  queued: '#3b82f6',      // blue-500
  running: '#f97316',     // orange-500
  succeeded: '#22c55e',   // green-500
  deadLettered: '#ef4444', // red-500
  // Dark mode variants (lighter for visibility on dark backgrounds)
  queuedDark: '#60a5fa',      // blue-400
  runningDark: '#fb923c',     // orange-400
  succeededDark: '#4ade80',   // green-400
  deadLetteredDark: '#f87171', // red-400
};

// Get colors based on current theme
const getChartColors = (isDark: boolean) => ({
  queued: isDark ? CHART_COLORS.queuedDark : CHART_COLORS.queued,
  running: isDark ? CHART_COLORS.runningDark : CHART_COLORS.running,
  succeeded: isDark ? CHART_COLORS.succeededDark : CHART_COLORS.succeeded,
  deadLettered: isDark ? CHART_COLORS.deadLetteredDark : CHART_COLORS.deadLettered,
});

// Hook to detect dark mode
import { useEffect, useState } from 'react';
function useIsDark() {
  const [isDark, setIsDark] = useState(false);
  useEffect(() => {
    const checkDark = () => setIsDark(document.documentElement.classList.contains('dark'));
    checkDark();
    const observer = new MutationObserver(checkDark);
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['class'] });
    return () => observer.disconnect();
  }, []);
  return isDark;
}

export const MetricsCharts = ({ metrics }: MetricsChartsProps) => {
  const isDark = useIsDark();
  const colors = getChartColors(isDark);

  const totalQueued = Object.values(metrics.queuedByType).reduce((a, b) => a + b, 0);
  const totalRunning = Object.values(metrics.runningByType).reduce((a, b) => a + b, 0);

  const throughputData = metrics.throughputHistory.slice(-20).map((point) => ({
    timestamp: new Date(point.timestamp).toLocaleTimeString(),
    value: point.value,
  }));

  const latencyData = metrics.latencyHistory.slice(-20).map((point) => ({
    timestamp: new Date(point.timestamp).toLocaleTimeString(),
    value: point.value,
  }));

  const queueDepthData = [
    { type: 'Queued', count: totalQueued },
    { type: 'Running', count: totalRunning },
  ];

  const retryRateData = metrics.retryRateHistory.slice(-20).map((point) => ({
    timestamp: new Date(point.timestamp).toLocaleTimeString(),
    value: point.value,
  }));

  return (
    <div className="space-y-6">
      <h2 className="text-xl font-semibold">Metrics Dashboard</h2>

      {/* Summary Cards - using theme-aware colors */}
      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Queued Jobs</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-3xl font-bold" style={{ color: colors.queued }}>{totalQueued}</div>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Running Jobs</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-3xl font-bold" style={{ color: colors.running }}>{totalRunning}</div>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Succeeded</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-3xl font-bold" style={{ color: colors.succeeded }}>{metrics.succeededTotal}</div>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Dead Lettered</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-3xl font-bold" style={{ color: colors.deadLettered }}>{metrics.deadLetteredTotal}</div>
          </CardContent>
        </Card>
      </div>

      {/* Charts - using explicit theme-aware colors */}
      <div className="grid gap-6 lg:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle>Throughput (jobs/sec)</CardTitle>
          </CardHeader>
          <CardContent>
            <AreaChart
              data={throughputData.length > 0 ? throughputData : [{ timestamp: '00:00', value: 0 }]}
              index="timestamp"
              categories={['value']}
              colors={[colors.queued]}
              className="h-64"
            />
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Job Latency (seconds)</CardTitle>
          </CardHeader>
          <CardContent>
            <AreaChart
              data={latencyData.length > 0 ? latencyData : [{ timestamp: '00:00', value: 0 }]}
              index="timestamp"
              categories={['value']}
              colors={[colors.running]}
              className="h-64"
            />
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Queue Depth by Status</CardTitle>
          </CardHeader>
          <CardContent>
            <DonutChart
              data={queueDepthData}
              index="type"
              category="count"
              colors={[colors.queued, colors.running]}
              className="h-64"
            />
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Retry Rate</CardTitle>
          </CardHeader>
          <CardContent>
            <BarChart
              data={retryRateData.length > 0 ? retryRateData : [{ timestamp: '00:00', value: 0 }]}
              index="timestamp"
              categories={['value']}
              colors={[colors.deadLettered]}
              className="h-64"
            />
          </CardContent>
        </Card>
      </div>

      {/* Worker In-Flight */}
      {Object.keys(metrics.workerInFlight).length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle>Worker In-Flight Jobs</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="grid gap-2 md:grid-cols-4">
              {Object.entries(metrics.workerInFlight).map(([worker, count]) => (
                <div key={worker} className="p-3 bg-muted rounded-lg">
                  <div className="text-sm text-muted-foreground">{worker}</div>
                  <div className="text-2xl font-bold">{count}</div>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>
      )}
    </div>
  );
};