import { BrowserRouter, Routes, Route, NavLink } from 'react-router-dom'
import { LayoutDashboard, List, FlaskConical, Settings, Menu, X, Sun, Moon } from 'lucide-react'
import { useState } from 'react'
import { Button } from './components/ui/Button'
import { Card, CardContent, CardHeader, CardTitle } from './components/ui/Card'
import { StatusCards } from './components/StatusCards'
import { JobTable } from './components/JobTable'
import { MetricsCharts } from './components/MetricsCharts'
import { TestScenarioPanel } from './components/TestScenarioPanel'
import { JobDispatchForm } from './components/JobDispatchForm'
import { JobDetailModal } from './components/JobDetailModal'
import { testScenarios } from './utils/testScenarios'
import { useJobs, useBatchCreateJobs } from './api/jobs'
import { useMetrics } from './api/metrics'
import { useAdaptivePolling } from './hooks/useAdaptivePolling'
import { useDarkMode } from './hooks/useDarkMode'
import type { Job } from './types/job'

const Sidebar = ({ isOpen, onClose }: { isOpen: boolean; onClose: () => void }) => {
  const navItems = [
    { path: '/', label: 'Dashboard', icon: LayoutDashboard },
    { path: '/jobs', label: 'Jobs', icon: List },
    { path: '/test-scenarios', label: 'Test Scenarios', icon: FlaskConical },
    { path: '/settings', label: 'Settings', icon: Settings },
  ]

  return (
    <>
      {isOpen && (
        <div
          className="fixed inset-0 bg-black/50 z-40 lg:hidden"
          onClick={onClose}
          aria-hidden="true"
        />
      )}
      <aside
        className={`fixed top-0 left-0 z-50 h-full w-64 bg-white dark:bg-gray-900 border-r border-gray-200 dark:border-gray-700 transition-transform duration-300 lg:translate-x-0 ${
          isOpen ? 'translate-x-0' : '-translate-x-full'
        }`}
      >
        <div className="flex h-full flex-col">
          <div className="flex h-16 items-center justify-between px-4 border-b border-gray-200 dark:border-gray-700">
            <h1 className="text-xl font-bold text-gray-900 dark:text-white">JobQueue</h1>
            <Button variant="ghost" size="icon" className="lg:hidden" onClick={onClose}>
              <X className="h-5 w-5" />
            </Button>
          </div>
          <nav className="flex-1 space-y-1 p-4">
            {navItems.map(item => (
              <NavLink
                key={item.path}
                to={item.path}
                onClick={onClose}
                className={({ isActive }) =>
                  `flex items-center gap-3 px-3 py-2 rounded-lg text-sm font-medium transition-colors ${
                    isActive
                      ? 'bg-blue-50 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300'
                      : 'text-gray-600 hover:bg-gray-100 dark:text-gray-300 dark:hover:bg-gray-800'
                  }`
                }
              >
                <item.icon className="h-5 w-5" />
                {item.label}
              </NavLink>
            ))}
          </nav>
        </div>
      </aside>
    </>
  )
}

const Header = ({ onMenuClick }: { onMenuClick: () => void }) => {
  const [isDark, setIsDark] = useDarkMode();

  return (
    <header className="sticky top-0 z-30 h-16 bg-white/80 dark:bg-gray-900/80 backdrop-blur-sm border-b border-gray-200 dark:border-gray-700">
      <div className="flex h-full items-center justify-between px-4 lg:px-6">
        <Button variant="ghost" size="icon" className="lg:hidden" onClick={onMenuClick}>
          <Menu className="h-5 w-5" />
        </Button>
        <div className="flex items-center gap-4">
          <span className="text-sm text-muted-foreground hidden sm:block">
            Distributed Job Queue
          </span>
          {/* Mode indicator */}
          <span className="hidden sm:inline-flex items-center gap-1.5 px-2 py-1 text-xs font-medium rounded bg-muted text-muted-foreground">
            {isDark ? '🌙 Dark' : '☀️ Light'}
          </span>
          <Button
            variant="ghost"
            size="icon"
            onClick={() => setIsDark(!isDark)}
            aria-label={isDark ? 'Switch to light mode' : 'Switch to dark mode'}
          >
            {isDark ? <Sun className="h-5 w-5" /> : <Moon className="h-5 w-5" />}
          </Button>
        </div>
      </div>
    </header>
  )
}

const DashboardPage = () => {
  const { data: jobs = [], isLoading: jobsLoading } = useJobs()
  const { data: metrics } = useMetrics()

  useAdaptivePolling()

  if (jobsLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600" />
      </div>
    )
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold">Dashboard</h1>
        <p className="text-muted-foreground">Real-time job queue overview</p>
      </div>
      <StatusCards jobs={jobs} />
      <MetricsCharts metrics={metrics!} />
    </div>
  )
}

const JobsPage = () => {
  const { data: jobs = [], isLoading } = useJobs()
  const [selectedJob, setSelectedJob] = useState<Job | null>(null)
  const batchCreateJobs = useBatchCreateJobs()

  useAdaptivePolling()

  const handleDispatch = async (newJobs: any[]) => {
    try {
      await batchCreateJobs.mutateAsync(newJobs)
    } catch (error) {
      alert(`Failed to dispatch jobs: ${error}`)
    }
  }

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600" />
      </div>
    )
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold">Jobs</h1>
          <p className="text-muted-foreground">Manage and monitor job queue</p>
        </div>
        <JobDispatchForm onDispatch={handleDispatch} />
      </div>
      <JobTable
        jobs={jobs}
        onJobClick={setSelectedJob}
      />
      <JobDetailModal
        job={selectedJob}
        onClose={() => setSelectedJob(null)}
      />
    </div>
  )
}

const TestScenariosPage = () => {
  const [runningScenarioId, setRunningScenarioId] = useState<string | undefined>(undefined)

  useAdaptivePolling()

  const handleRunScenario = (scenario: any) => {
    setRunningScenarioId(scenario.id)
    setTimeout(() => setRunningScenarioId(undefined), 3000)
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold">Test Scenarios</h1>
        <p className="text-muted-foreground">Pre-defined test scenarios for validating queue behavior</p>
      </div>
      <TestScenarioPanel
        scenarios={testScenarios}
        onRunScenario={handleRunScenario}
        runningScenarioId={runningScenarioId}
      />
    </div>
  )
}

const SettingsPage = () => (
  <div className="space-y-6">
    <div>
      <h1 className="text-2xl font-bold">Settings</h1>
      <p className="text-muted-foreground">Configure application settings</p>
    </div>
    <Card>
      <CardHeader>
        <CardTitle>API Configuration</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        <div>
          <label className="block text-sm font-medium mb-1">API Base URL</label>
          <input
            type="text"
            value="/api"
            readOnly
            className="w-full px-3 py-2 bg-gray-100 dark:bg-gray-800 border rounded-md text-sm"
          />
        </div>
        <p className="text-sm text-muted-foreground">
          API calls are proxied through Vite to the backend service.
        </p>
      </CardContent>
    </Card>
    <Card>
      <CardHeader>
        <CardTitle>Polling Configuration</CardTitle>
      </CardHeader>
      <CardContent className="space-y-2 text-sm">
        <div className="grid grid-cols-2 gap-4">
          <div className="p-3 bg-gray-50 dark:bg-gray-800 rounded">
            <div className="font-medium">Fast (running jobs)</div>
            <div className="text-muted-foreground">500ms</div>
          </div>
          <div className="p-3 bg-gray-50 dark:bg-gray-800 rounded">
            <div className="font-medium">Medium (queued jobs)</div>
            <div className="text-muted-foreground">2s</div>
          </div>
          <div className="p-3 bg-gray-50 dark:bg-gray-800 rounded">
            <div className="font-medium">Slow (idle)</div>
            <div className="text-muted-foreground">5s</div>
          </div>
        </div>
      </CardContent>
    </Card>
  </div>
)

const Layout = () => {
  const [sidebarOpen, setSidebarOpen] = useState(false)

  return (
    <div className="min-h-screen bg-gray-50 dark:bg-gray-950">
      <Sidebar isOpen={sidebarOpen} onClose={() => setSidebarOpen(false)} />
      <div className="lg:pl-64">
        <Header onMenuClick={() => setSidebarOpen(true)} />
        <main className="p-4 lg:p-6">
          <Routes>
            <Route path="/" element={<DashboardPage />} />
            <Route path="/jobs" element={<JobsPage />} />
            <Route path="/test-scenarios" element={<TestScenariosPage />} />
            <Route path="/settings" element={<SettingsPage />} />
          </Routes>
        </main>
      </div>
    </div>
  )
}

const App = () => {
  return (
    <BrowserRouter>
      <Layout />
    </BrowserRouter>
  )
}

export default App