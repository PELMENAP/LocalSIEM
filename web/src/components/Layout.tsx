import { NavLink, Outlet } from 'react-router'
import { useAuth } from '../auth/AuthContext'
import { useRealtime } from '../realtime/RealtimeContext'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import type { DashboardSummary } from '../api/types'

const NAV = [
  { to: '/', label: 'Dashboard', icon: 'M3 3h7v9H3zM14 3h7v5h-7zM14 12h7v9h-7zM3 16h7v5H3z' },
  { to: '/events', label: 'Event Explorer', icon: 'M4 6h16M4 12h16M4 18h10' },
  { to: '/detection', label: 'Threat Detection', icon: 'M12 3 4 6v6c0 5 3.5 8.5 8 9.5 4.5-1 8-4.5 8-9.5V6z' },
  { to: '/alerts', label: 'Alerts', icon: 'M12 3 2 20h20zM12 10v4M12 17h.01' },
  { to: '/incidents', label: 'Incidents', icon: 'M9 4h6l1 3h4v13H4V7h4zM9 13h6M12 10v6' },
  { to: '/simulator', label: 'Event Simulator', icon: 'M5 4l14 8-14 8z' },
  { to: '/settings', label: 'Settings', icon: 'M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6zM19 12a7 7 0 0 0-.1-1.2l2-1.6-2-3.4-2.4 1a7 7 0 0 0-2-1.2L14 3h-4l-.5 2.6a7 7 0 0 0-2 1.2l-2.4-1-2 3.4 2 1.6A7 7 0 0 0 5 12c0 .4 0 .8.1 1.2l-2 1.6 2 3.4 2.4-1a7 7 0 0 0 2 1.2L10 21h4l.5-2.6a7 7 0 0 0 2-1.2l2.4 1 2-3.4-2-1.6c.1-.4.1-.8.1-1.2z' }
]

const CONNECTION_LABEL = {
  connected: { text: 'Realtime', color: '#22c55e' },
  connecting: { text: 'Подключение', color: '#eab308' },
  reconnecting: { text: 'Переподключение', color: '#eab308' },
  disconnected: { text: 'Нет соединения', color: '#ef4444' }
}

export function Layout() {
  const { user, logout } = useAuth()
  const realtime = useRealtime()
  const summary = useQuery({
    queryKey: ['dashboard', 'summary'],
    queryFn: () => api.get<DashboardSummary>('/api/dashboard/summary'),
    refetchInterval: 30_000
  })
  const connection = CONNECTION_LABEL[realtime.status]
  const newAlerts = summary.data?.alertsNew ?? 0

  return (
    <div className="flex h-full">
      <aside className="flex w-[216px] shrink-0 flex-col border-r border-line bg-panel">
        <div className="flex h-14 items-center gap-2.5 border-b border-line px-4">
          <img src="/favicon.svg" alt="" className="h-7 w-7" />
          <div>
            <div className="text-sm font-semibold tracking-wide text-ink-strong">LocalSIEM</div>
            <div className="text-[10px] uppercase tracking-[0.14em] text-faint">security monitoring</div>
          </div>
        </div>
        <nav className="flex flex-1 flex-col gap-0.5 p-2">
          {NAV.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.to === '/'}
              className={({ isActive }) =>
                `flex h-8 items-center gap-2.5 rounded-md px-2.5 text-[13px] transition-colors ${
                  isActive ? 'bg-panel-3 text-ink-strong' : 'text-muted hover:bg-panel-2 hover:text-ink'
                }`
              }
            >
              <svg viewBox="0 0 24 24" className="h-4 w-4 shrink-0" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
                <path d={item.icon} />
              </svg>
              <span className="flex-1">{item.label}</span>
              {item.to === '/alerts' && newAlerts > 0 && (
                <span className="rounded bg-sev-critical/20 px-1.5 text-[10px] font-semibold text-red-300 tabular-nums">{newAlerts}</span>
              )}
            </NavLink>
          ))}
        </nav>
        <div className="border-t border-line p-3 text-xs">
          <div className="mb-2 flex items-center gap-2 text-muted">
            <span className="h-2 w-2 rounded-full" style={{ background: connection.color }} />
            <span>{connection.text}</span>
            <span className="ml-auto tabular-nums text-faint">{realtime.eventsPerSecond > 0 ? `${realtime.eventsPerSecond} соб/с` : ''}</span>
          </div>
          <div className="flex items-center justify-between gap-2">
            <div className="min-w-0">
              <div className="truncate text-ink">{user?.displayName}</div>
              <div className="text-[11px] text-faint">{user?.role === 'Administrator' ? 'Administrator' : 'Security Analyst'}</div>
            </div>
            <button className="btn btn-ghost h-7 px-2 text-xs" onClick={logout}>Выйти</button>
          </div>
        </div>
      </aside>
      <main className="min-w-0 flex-1 overflow-auto">
        <div className="mx-auto max-w-[1600px] p-5">
          <Outlet />
        </div>
      </main>
    </div>
  )
}
