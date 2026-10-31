import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import type { EventListItem, PagedResult, PurgeResult, ScenarioInfo, SimulationState, Source } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { EventDetailsDrawer, EventsTable } from '../components/EventViews'
import { useToasts } from '../components/Toasts'
import { Empty, ErrorBox, Modal, PageHeader, Panel, Spinner, errorText } from '../components/ui'
import { useRealtime } from '../realtime/RealtimeContext'
import { formatNumber, formatTime } from '../lib/format'

const CONFIRM_PHRASE = 'DELETE DEMO DATA'

export function SimulatorPage() {
  const { isAdmin } = useAuth()
  const toasts = useToasts()
  const realtime = useRealtime()
  const queryClient = useQueryClient()
  const [scenarioKey, setScenarioKey] = useState('brute-force')
  const [count, setCount] = useState('')
  const [rate, setRate] = useState('')
  const [sourceIp, setSourceIp] = useState('')
  const [username, setUsername] = useState('')
  const [purgeOpen, setPurgeOpen] = useState(false)
  const [openEvent, setOpenEvent] = useState<string | null>(null)
  const [received, setReceived] = useState(0)

  const scenarios = useQuery({ queryKey: ['simulator', 'scenarios'], queryFn: () => api.get<ScenarioInfo[]>('/api/simulator/scenarios'), staleTime: Infinity })
  const state = useQuery({
    queryKey: ['simulator', 'state'],
    queryFn: () => api.get<SimulationState>('/api/simulator/runs/current'),
    refetchInterval: (query) => (query.state.data?.state === 'Running' ? 1000 : false)
  })
  const sources = useQuery({ queryKey: ['sources'], queryFn: () => api.get<Source[]>('/api/sources') })
  const simulatorSource = sources.data?.find((s) => s.kind === 'Simulator')
  const feed = useQuery({
    queryKey: ['events', 'simulator-feed', simulatorSource?.id],
    queryFn: () => api.get<PagedResult<EventListItem>>('/api/events', { sourceId: [simulatorSource!.id], pageSize: 25 }),
    enabled: Boolean(simulatorSource)
  })

  useEffect(() => realtime.subscribe((batch) => setReceived((r) => r + batch.count)), [realtime])

  const scenario = useMemo(() => scenarios.data?.find((s) => s.key === scenarioKey), [scenarios.data, scenarioKey])
  useEffect(() => {
    if (!scenario) return
    setCount(String(scenario.defaultCount))
    setRate(String(scenario.defaultRate))
    setSourceIp(scenario.defaultSourceIp ?? '')
    setUsername(scenario.defaultUsername ?? '')
  }, [scenario])

  const start = useMutation({
    mutationFn: () =>
      api.post<SimulationState>('/api/simulator/runs', {
        scenario: scenarioKey,
        count: Number(count),
        ratePerSecond: Number(rate),
        sourceIp: sourceIp || null,
        username: username || null
      }),
    onSuccess: (s) => {
      setReceived(0)
      queryClient.setQueryData(['simulator', 'state'], s)
    },
    onError: (error) => toasts.error(errorText(error))
  })
  const stop = useMutation({
    mutationFn: (runId: string) => api.del<SimulationState>(`/api/simulator/runs/${runId}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['simulator'] }),
    onError: (error) => toasts.error(errorText(error))
  })

  const current = { ...state.data, ...(realtime.simulator?.runId === state.data?.runId ? realtime.simulator : {}) } as SimulationState | undefined
  const running = current?.state === 'Running'
  const progress = current && current.total > 0 ? Math.round((current.sent / current.total) * 100) : 0

  return (
    <>
      <PageHeader
        title="Event Simulator"
        subtitle="События генерируются через тот же сервис приёма, что и HTTP API, и проходят нормализацию и Detection Engine"
        actions={isAdmin && <button className="btn btn-danger" onClick={() => setPurgeOpen(true)}>Очистить демо-данные</button>}
      />
      <div className="mb-3 grid grid-cols-1 gap-3 xl:grid-cols-3">
        <Panel title="Сценарии" className="xl:col-span-2" bodyClassName="p-3">
          {scenarios.isLoading && <Spinner />}
          {scenarios.error && <ErrorBox error={scenarios.error} />}
          <div className="grid grid-cols-1 gap-2 md:grid-cols-2">
            {scenarios.data?.map((s, index) => (
              <button
                key={s.key}
                type="button"
                onClick={() => setScenarioKey(s.key)}
                className={`rounded-md border p-3 text-left transition-colors ${
                  s.key === scenarioKey ? 'border-accent/70 bg-accent/10' : 'border-line bg-panel-2 hover:border-line-strong'
                }`}
              >
                <div className="mb-1 flex items-center gap-2">
                  <span className="mono text-[11px] text-faint">{index + 1}</span>
                  <span className="font-medium text-ink-strong">{s.title}</span>
                </div>
                <div className="mb-2 text-xs text-muted">{s.description}</div>
                <div className="text-[11px] text-faint">Ожидается: <span className="text-ink/80">{s.expectedAlerts}</span></div>
              </button>
            ))}
          </div>
        </Panel>
        <Panel title="Параметры запуска">
          <div className="flex flex-col gap-3">
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="label">Количество событий</label>
                <input type="number" min={1} max={10000} className="field w-full" value={count} onChange={(e) => setCount(e.target.value)} />
              </div>
              <div>
                <label className="label">Скорость, соб/с</label>
                <input type="number" min={0.1} step={0.1} className="field w-full" value={rate} onChange={(e) => setRate(e.target.value)} />
              </div>
            </div>
            <div>
              <label className="label">IP-адрес</label>
              <input className="field mono w-full" placeholder="по умолчанию сценария" value={sourceIp} onChange={(e) => setSourceIp(e.target.value)} />
            </div>
            <div>
              <label className="label">Учётная запись</label>
              <input className="field w-full" placeholder="по умолчанию сценария" value={username} onChange={(e) => setUsername(e.target.value)} />
            </div>
            <div className="flex gap-2">
              <button className="btn btn-primary flex-1" disabled={running || start.isPending || !scenario} onClick={() => start.mutate()}>Запустить</button>
              <button className="btn flex-1" disabled={!running || stop.isPending} onClick={() => current?.runId && stop.mutate(current.runId)}>Остановить</button>
            </div>
            {current && current.state !== 'Idle' && (
              <div className="rounded-md border border-line bg-panel-2 p-3">
                <div className="mb-2 flex justify-between text-xs">
                  <span className="text-ink">{scenarios.data?.find((s) => s.key === current.scenario)?.title ?? current.scenario}</span>
                  <span className={running ? 'text-accent' : current.state === 'Failed' ? 'text-red-300' : 'text-muted'}>{current.state}</span>
                </div>
                <div className="mb-1.5 h-1.5 overflow-hidden rounded bg-panel-3">
                  <div className="h-full bg-accent transition-all" style={{ width: `${progress}%` }} />
                </div>
                <div className="flex justify-between text-[11px] text-muted tabular-nums">
                  <span>{formatNumber(current.sent)} / {formatNumber(current.total)} событий</span>
                  <span>старт {formatTime(current.startedAt)}</span>
                </div>
                {current.error && <div className="mt-2 text-xs text-red-300">{current.error}</div>}
              </div>
            )}
            <div className="text-[11px] text-faint">Принято системой с открытия страницы: <span className="tabular-nums text-ink/80">{formatNumber(received)}</span> событий</div>
          </div>
        </Panel>
      </div>
      <Panel title="Поток событий симулятора" bodyClassName="">
        {!simulatorSource && <Empty>Источник симулятора не найден</Empty>}
        {feed.data && feed.data.items.length === 0 && <Empty>Событий пока нет — запустите сценарий</Empty>}
        {feed.data && feed.data.items.length > 0 && <EventsTable items={feed.data.items} onOpen={setOpenEvent} />}
      </Panel>
      {openEvent && <EventDetailsDrawer id={openEvent} onClose={() => setOpenEvent(null)} />}
      {purgeOpen && <PurgeModal onClose={() => setPurgeOpen(false)} />}
    </>
  )
}

function PurgeModal({ onClose }: { onClose: () => void }) {
  const [phrase, setPhrase] = useState('')
  const queryClient = useQueryClient()
  const toasts = useToasts()
  const purge = useMutation({
    mutationFn: () => api.post<PurgeResult>('/api/simulator/purge', { confirm: phrase }),
    onSuccess: (result) => {
      toasts.success(`Удалено: событий ${result.events}, предупреждений ${result.alerts}, инцидентов ${result.incidents}`)
      queryClient.invalidateQueries()
      onClose()
    }
  })
  return (
    <Modal
      title="Очистка демонстрационных данных"
      onClose={onClose}
      footer={
        <>
          <button className="btn" onClick={onClose}>Отмена</button>
          <button className="btn btn-danger" disabled={phrase !== CONFIRM_PHRASE || purge.isPending} onClick={() => purge.mutate()}>Удалить</button>
        </>
      }
    >
      <p className="mb-3 text-[13px] leading-relaxed">
        Будут удалены все события симулятора, предупреждения с их участием и инциденты, состоящие только из таких предупреждений. Данные из HTTP API и Windows Collector не затрагиваются. Операция необратима.
      </p>
      <label className="label">Для подтверждения введите {CONFIRM_PHRASE}</label>
      <input className="field mono w-full" value={phrase} onChange={(e) => setPhrase(e.target.value)} autoFocus />
      {purge.error && <ErrorBox error={purge.error} />}
    </Modal>
  )
}
