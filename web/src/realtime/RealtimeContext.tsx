import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from 'react'
import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { getToken } from '../api/client'
import type { AlertStatus, IncidentStatus, Severity, SimulationState, SourceStatus } from '../api/types'
import { useToasts } from '../components/Toasts'

export interface EventsIngested {
  count: number
  bySeverity: Partial<Record<Severity, number>>
  lastTimestamp: string
}

interface AlertCreated {
  id: string
  title: string
  severity: Severity
  ruleCode: string
  detectedAt: string
}

type ConnectionStatus = 'connecting' | 'connected' | 'reconnecting' | 'disconnected'

interface RealtimeState {
  status: ConnectionStatus
  eventsPerSecond: number
  lastBatch: EventsIngested | null
  simulator: Partial<SimulationState> | null
  subscribe: (listener: (batch: EventsIngested) => void) => () => void
}

const RealtimeContext = createContext<RealtimeState | null>(null)

export function RealtimeProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const toasts = useToasts()
  const [status, setStatus] = useState<ConnectionStatus>('connecting')
  const [eventsPerSecond, setEventsPerSecond] = useState(0)
  const [lastBatch, setLastBatch] = useState<EventsIngested | null>(null)
  const [simulator, setSimulator] = useState<Partial<SimulationState> | null>(null)
  const listeners = useRef(new Set<(batch: EventsIngested) => void>())
  const lastEventsRefresh = useRef(0)

  useEffect(() => {
    const connection: HubConnection = new HubConnectionBuilder()
      .withUrl('/hubs/siem', { accessTokenFactory: () => getToken() ?? '' })
      .withAutomaticReconnect([0, 1000, 3000, 5000, 10000, 20000])
      .configureLogging(LogLevel.Warning)
      .build()

    let decay: ReturnType<typeof setTimeout> | undefined
    const invalidate = (...keys: string[]) => keys.forEach((key) => queryClient.invalidateQueries({ queryKey: [key] }))

    connection.on('EventsIngested', (batch: EventsIngested) => {
      setLastBatch(batch)
      setEventsPerSecond(batch.count)
      clearTimeout(decay)
      decay = setTimeout(() => setEventsPerSecond(0), 2500)
      listeners.current.forEach((listener) => listener(batch))
      const now = Date.now()
      if (now - lastEventsRefresh.current > 2000) {
        lastEventsRefresh.current = now
        invalidate('dashboard', 'events')
      }
    })
    connection.on('AlertCreated', (alert: AlertCreated) => {
      toasts.push({ tone: alert.severity, title: `Угроза: ${alert.title}`, body: alert.ruleCode, link: `/alerts?open=${alert.id}` })
      invalidate('alerts', 'dashboard', 'rules')
    })
    connection.on('AlertUpdated', (_: { id: string; status: AlertStatus; eventCount: number }) => invalidate('alerts', 'alert', 'dashboard'))
    connection.on('IncidentCreated', (incident: { id: string; number: number; title: string; severity: Severity }) => {
      toasts.push({ tone: incident.severity, title: `Инцидент INC-${incident.number}`, body: incident.title, link: `/incidents/${incident.id}` })
      invalidate('incidents', 'dashboard', 'alerts')
    })
    connection.on('IncidentUpdated', (_: { id: string; status: IncidentStatus; assigneeId: string | null }) =>
      invalidate('incidents', 'incident', 'dashboard')
    )
    connection.on('SourceStatusChanged', (source: { id: string; name: string; status: SourceStatus }) => {
      toasts.push({ tone: source.status === 'Online' ? 'Info' : 'Medium', title: `Источник «${source.name}»`, body: `Статус: ${source.status}` })
      invalidate('sources', 'dashboard')
    })
    connection.on('SimulatorStateChanged', (state: Partial<SimulationState>) => {
      setSimulator(state)
      invalidate('simulator')
    })

    connection.onreconnecting(() => setStatus('reconnecting'))
    connection.onreconnected(() => {
      setStatus('connected')
      queryClient.invalidateQueries()
    })
    connection.onclose(() => setStatus('disconnected'))

    let cancelled = false
    const start = async () => {
      while (!cancelled && connection.state === HubConnectionState.Disconnected) {
        try {
          setStatus('connecting')
          await connection.start()
          setStatus('connected')
        } catch {
          setStatus('disconnected')
          await new Promise((resolve) => setTimeout(resolve, 5000))
        }
      }
    }
    void start()

    return () => {
      cancelled = true
      clearTimeout(decay)
      void connection.stop()
    }
  }, [queryClient, toasts])

  const value: RealtimeState = {
    status,
    eventsPerSecond,
    lastBatch,
    simulator,
    subscribe: (listener) => {
      listeners.current.add(listener)
      return () => {
        listeners.current.delete(listener)
      }
    }
  }

  return <RealtimeContext.Provider value={value}>{children}</RealtimeContext.Provider>
}

export function useRealtime(): RealtimeState {
  const context = useContext(RealtimeContext)
  if (!context) throw new Error('RealtimeProvider is missing')
  return context
}
