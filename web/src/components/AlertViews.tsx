import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import type { AlertDetails, AlertListItem, AlertStatus, EventListItem, IncidentDetails, PagedResult, Severity, UserOption } from '../api/types'
import { formatDateTime, formatNumber } from '../lib/format'
import { EventDetailsDrawer, EventsTable } from './EventViews'
import { useToasts } from './Toasts'
import { Drawer, Empty, ErrorBox, KeyValue, Modal, Pagination, SeverityBadge, Spinner, StatusBadge, errorText } from './ui'

const ACTIONS: Record<AlertStatus, { to: AlertStatus; label: string }[]> = {
  New: [
    { to: 'Acknowledged', label: 'Взять в работу' },
    { to: 'Resolved', label: 'Решено' }
  ],
  Acknowledged: [{ to: 'Resolved', label: 'Решено' }],
  Resolved: [{ to: 'Acknowledged', label: 'Переоткрыть' }]
}

export function useAlertStatus() {
  const queryClient = useQueryClient()
  const toasts = useToasts()
  return useMutation({
    mutationFn: ({ id, status, version }: { id: string; status: AlertStatus; version: number }) =>
      api.patch<AlertDetails>(`/api/alerts/${id}/status`, { status, version }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['alerts'] })
      queryClient.invalidateQueries({ queryKey: ['alert'] })
      queryClient.invalidateQueries({ queryKey: ['dashboard'] })
    },
    onError: (error) => toasts.error(errorText(error))
  })
}

export function AlertDetailsDrawer({ id, onClose, onCreateIncident }: { id: string; onClose: () => void; onCreateIncident?: (alert: AlertDetails) => void }) {
  const [page, setPage] = useState(1)
  const [openEvent, setOpenEvent] = useState<string | null>(null)
  const alert = useQuery({ queryKey: ['alert', id], queryFn: () => api.get<AlertDetails>(`/api/alerts/${id}`) })
  const events = useQuery({
    queryKey: ['alert', id, 'events', page],
    queryFn: () => api.get<PagedResult<EventListItem>>(`/api/alerts/${id}/events`, { page, pageSize: 50 }),
    placeholderData: keepPreviousData
  })
  const status = useAlertStatus()
  const a = alert.data

  return (
    <Drawer
      title={a ? <span className="flex items-center gap-2"><SeverityBadge severity={a.severity} /> <span className="truncate">{a.title}</span></span> : 'Предупреждение'}
      onClose={onClose}
      width={860}
      actions={
        a && (
          <>
            {ACTIONS[a.status].map((action) => (
              <button key={action.to} className="btn h-7" disabled={status.isPending} onClick={() => status.mutate({ id: a.id, status: action.to, version: a.version })}>
                {action.label}
              </button>
            ))}
            {!a.incidentId && onCreateIncident && (
              <button className="btn btn-primary h-7" onClick={() => onCreateIncident(a)}>Создать инцидент</button>
            )}
          </>
        )
      }
    >
      {alert.isLoading && <Spinner />}
      {alert.error && <ErrorBox error={alert.error} />}
      {a && (
        <div className="flex flex-col gap-5">
          <div className="grid grid-cols-1 gap-5 lg:grid-cols-2">
            <KeyValue
              items={[
                ['Статус', <StatusBadge status={a.status} />],
                ['Правило', `${a.ruleName} (${a.ruleCode})`],
                ['Тип правила', `${a.ruleType} · ${a.ruleEventType}`],
                ['Порог / окно', `${a.threshold} за ${a.windowSeconds} с`],
                ['Обнаружено', formatDateTime(a.detectedAt)],
                ['Первое событие', formatDateTime(a.firstEventAt)],
                ['Последнее событие', formatDateTime(a.lastEventAt)],
                ['Событий', `${formatNumber(a.eventCount)} (связано: ${formatNumber(a.linkedEventCount)})`],
                ['Изменил статус', a.statusChangedBy ? `${a.statusChangedBy}, ${formatDateTime(a.statusChangedAt)}` : '—'],
                [
                  'Инцидент',
                  a.incidentId ? (
                    <Link to={`/incidents/${a.incidentId}`} className="text-accent hover:underline">INC-{a.incidentNumber}</Link>
                  ) : (
                    'не привязано'
                  )
                ]
              ]}
            />
            <div>
              <div className="panel-title mb-2">Причина срабатывания</div>
              <div className="mb-3 rounded-md border border-line bg-panel-2 p-3 text-[12.5px] leading-relaxed whitespace-pre-line">{a.description}</div>
              <div className="panel-title mb-2">Признаки группировки</div>
              <div className="flex flex-wrap gap-1.5">
                {Object.entries(a.groupValues).map(([key, value]) => (
                  <span key={key} className="mono rounded border border-line bg-panel-2 px-2 py-0.5 text-[11.5px]">
                    <span className="text-muted">{key}=</span>{value}
                  </span>
                ))}
              </div>
            </div>
          </div>
          <div className="panel">
            <div className="flex h-9 items-center border-b border-line px-3 panel-title">Связанные события</div>
            {events.data && events.data.items.length === 0 && <Empty>Связанные события удалены</Empty>}
            {events.data && events.data.items.length > 0 && <EventsTable items={events.data.items} onOpen={setOpenEvent} compact />}
            {events.data && <Pagination page={page} pageSize={50} total={events.data.totalCount} onPage={setPage} />}
          </div>
        </div>
      )}
      {openEvent && <EventDetailsDrawer id={openEvent} onClose={() => setOpenEvent(null)} />}
    </Drawer>
  )
}

export function CreateIncidentModal({ alerts, onClose }: { alerts: Pick<AlertListItem, 'id' | 'title' | 'severity'>[]; onClose: () => void }) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const users = useQuery({ queryKey: ['user-options'], queryFn: () => api.get<UserOption[]>('/api/user-options') })
  const [title, setTitle] = useState(alerts.length === 1 ? alerts[0].title : `Атака: ${alerts.length} предупреждений`)
  const [description, setDescription] = useState('')
  const [assigneeId, setAssigneeId] = useState('')
  const [severity, setSeverity] = useState<Severity | ''>('')
  const create = useMutation({
    mutationFn: () =>
      api.post<IncidentDetails>('/api/incidents', {
        title,
        description,
        assigneeId: assigneeId || null,
        severity: severity || null,
        alertIds: alerts.map((a) => a.id)
      }),
    onSuccess: (incident) => {
      queryClient.invalidateQueries({ queryKey: ['alerts'] })
      queryClient.invalidateQueries({ queryKey: ['incidents'] })
      navigate(`/incidents/${incident.id}`)
    }
  })

  return (
    <Modal
      title="Новый инцидент"
      onClose={onClose}
      footer={
        <>
          <button className="btn" onClick={onClose}>Отмена</button>
          <button className="btn btn-primary" disabled={!title.trim() || create.isPending} onClick={() => create.mutate()}>
            {create.isPending ? 'Создание…' : 'Создать'}
          </button>
        </>
      }
    >
      <div className="flex flex-col gap-3">
        <div>
          <label className="label">Название</label>
          <input className="field w-full" value={title} onChange={(e) => setTitle(e.target.value)} />
        </div>
        <div>
          <label className="label">Описание</label>
          <textarea className="field w-full" rows={4} value={description} onChange={(e) => setDescription(e.target.value)} />
        </div>
        <div className="grid grid-cols-2 gap-3">
          <div>
            <label className="label">Ответственный</label>
            <select className="field w-full" value={assigneeId} onChange={(e) => setAssigneeId(e.target.value)}>
              <option value="">Не назначен</option>
              {users.data?.map((u) => <option key={u.id} value={u.id}>{u.displayName}</option>)}
            </select>
          </div>
          <div>
            <label className="label">Критичность</label>
            <select className="field w-full" value={severity} onChange={(e) => setSeverity(e.target.value as Severity | '')}>
              <option value="">Максимальная из предупреждений</option>
              {(['Critical', 'High', 'Medium', 'Low', 'Info'] as Severity[]).map((s) => <option key={s} value={s}>{s}</option>)}
            </select>
          </div>
        </div>
        <div>
          <div className="label">Предупреждения ({alerts.length})</div>
          <ul className="flex max-h-40 flex-col gap-1 overflow-auto">
            {alerts.map((a) => (
              <li key={a.id} className="flex items-center gap-2 text-xs"><SeverityBadge severity={a.severity} /> <span className="truncate">{a.title}</span></li>
            ))}
          </ul>
        </div>
        {create.error && <ErrorBox error={create.error} />}
      </div>
    </Modal>
  )
}
