import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import type { AlertListItem, Comment, EventListItem, HistoryEntry, IncidentDetails, IncidentStatus, PagedResult, UserOption } from '../api/types'
import { AlertDetailsDrawer } from '../components/AlertViews'
import { EventDetailsDrawer } from '../components/EventViews'
import { useToasts } from '../components/Toasts'
import { Empty, ErrorBox, KeyValue, Modal, Pagination, Panel, SeverityBadge, SeverityDot, Spinner, StatusBadge, errorText, statusLabel } from '../components/ui'
import { formatDateTime, formatNumber } from '../lib/format'

const TRANSITIONS: Record<IncidentStatus, { to: IncidentStatus; label: string }[]> = {
  Open: [
    { to: 'Investigating', label: 'Начать расследование' },
    { to: 'Resolved', label: 'Решён' }
  ],
  Investigating: [{ to: 'Resolved', label: 'Решён' }],
  Resolved: [
    { to: 'Closed', label: 'Закрыть' },
    { to: 'Investigating', label: 'Вернуть в расследование' }
  ],
  Closed: []
}

const HISTORY_LABEL: Record<HistoryEntry['action'], string> = {
  Created: 'Создан инцидент',
  StatusChanged: 'Изменён статус',
  Assigned: 'Назначен ответственный',
  AlertLinked: 'Привязано предупреждение',
  CommentAdded: 'Добавлен комментарий'
}

export function IncidentDetailsPage() {
  const { id = '' } = useParams()
  const queryClient = useQueryClient()
  const toasts = useToasts()
  const [timelinePage, setTimelinePage] = useState(1)
  const [comment, setComment] = useState('')
  const [openAlert, setOpenAlert] = useState<string | null>(null)
  const [openEvent, setOpenEvent] = useState<string | null>(null)
  const [linking, setLinking] = useState(false)

  const incident = useQuery({ queryKey: ['incident', id], queryFn: () => api.get<IncidentDetails>(`/api/incidents/${id}`) })
  const comments = useQuery({ queryKey: ['incident', id, 'comments'], queryFn: () => api.get<Comment[]>(`/api/incidents/${id}/comments`) })
  const history = useQuery({ queryKey: ['incident', id, 'history'], queryFn: () => api.get<HistoryEntry[]>(`/api/incidents/${id}/history`) })
  const timeline = useQuery({
    queryKey: ['incident', id, 'timeline', timelinePage],
    queryFn: () => api.get<PagedResult<EventListItem>>(`/api/incidents/${id}/timeline`, { page: timelinePage, pageSize: 100 }),
    placeholderData: keepPreviousData
  })
  const users = useQuery({ queryKey: ['user-options'], queryFn: () => api.get<UserOption[]>('/api/user-options') })

  const refresh = () => {
    queryClient.invalidateQueries({ queryKey: ['incident', id] })
    queryClient.invalidateQueries({ queryKey: ['incidents'] })
    queryClient.invalidateQueries({ queryKey: ['alerts'] })
  }
  const onError = (error: unknown) => {
    toasts.error(errorText(error))
    refresh()
  }

  const changeStatus = useMutation({
    mutationFn: (status: IncidentStatus) => api.patch(`/api/incidents/${id}/status`, { status, version: incident.data!.version }),
    onSuccess: refresh,
    onError
  })
  const assign = useMutation({
    mutationFn: (assigneeId: string | null) => api.patch(`/api/incidents/${id}/assignee`, { assigneeId, version: incident.data!.version }),
    onSuccess: refresh,
    onError
  })
  const addComment = useMutation({
    mutationFn: () => api.post(`/api/incidents/${id}/comments`, { body: comment }),
    onSuccess: () => {
      setComment('')
      refresh()
    },
    onError
  })

  if (incident.isLoading) return <Spinner />
  if (incident.error) return <ErrorBox error={incident.error} />
  const i = incident.data!
  const closed = i.status === 'Closed'

  return (
    <>
      <div className="mb-4 flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="mb-1 flex items-center gap-2 text-xs text-muted">
            <Link to="/incidents" className="hover:text-ink">Инциденты</Link>
            <span>/</span>
            <span className="mono">INC-{i.number}</span>
            {i.isDemo && <span className="rounded bg-panel-3 px-1.5 text-[10px] text-faint">demo</span>}
          </div>
          <h1 className="flex items-center gap-3 text-lg font-semibold text-ink-strong">
            <SeverityBadge severity={i.severity} />
            <span className="truncate">{i.title}</span>
            <StatusBadge status={i.status} />
          </h1>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {TRANSITIONS[i.status].map((t) => (
            <button
              key={t.to}
              className={`btn ${t.to === 'Resolved' || t.to === 'Closed' ? 'btn-primary' : ''}`}
              disabled={changeStatus.isPending}
              onClick={() => changeStatus.mutate(t.to)}
            >
              {t.label}
            </button>
          ))}
        </div>
      </div>

      <div className="grid grid-cols-1 gap-3 xl:grid-cols-3">
        <div className="flex min-w-0 flex-col gap-3 xl:col-span-2">
          <Panel title="Описание">
            <div className="mb-4 text-[13px] leading-relaxed whitespace-pre-wrap">{i.description || <span className="text-faint">Описание не задано</span>}</div>
            <KeyValue
              items={[
                ['Статус', statusLabel(i.status)],
                [
                  'Ответственный',
                  <select
                    className="field h-7"
                    disabled={closed || assign.isPending}
                    value={i.assigneeId ?? ''}
                    onChange={(e) => assign.mutate(e.target.value || null)}
                  >
                    <option value="">Не назначен</option>
                    {users.data?.map((u) => <option key={u.id} value={u.id}>{u.displayName}</option>)}
                  </select>
                ],
                ['Создал', `${i.createdByName}, ${formatDateTime(i.createdAt)}`],
                ['Обновлён', formatDateTime(i.updatedAt)],
                ['Решён', formatDateTime(i.resolvedAt)],
                ['Закрыт', formatDateTime(i.closedAt)]
              ]}
            />
          </Panel>

          <Panel
            title={`Связанные предупреждения (${i.alerts.length})`}
            actions={!closed && <button className="btn h-7 text-xs" onClick={() => setLinking(true)}>Привязать ещё</button>}
            bodyClassName=""
          >
            <table className="table">
              <thead>
                <tr>
                  <th>Обнаружено</th>
                  <th>Критичность</th>
                  <th>Предупреждение</th>
                  <th className="text-right">Событий</th>
                  <th>Статус</th>
                </tr>
              </thead>
              <tbody>
                {i.alerts.map((a) => (
                  <tr key={a.id} className="clickable" onClick={() => setOpenAlert(a.id)}>
                    <td className="tabular-nums whitespace-nowrap text-muted">{formatDateTime(a.detectedAt)}</td>
                    <td><SeverityBadge severity={a.severity} /></td>
                    <td className="max-w-[420px] truncate">{a.title}</td>
                    <td className="text-right tabular-nums">{formatNumber(a.eventCount)}</td>
                    <td><StatusBadge status={a.status} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </Panel>

          <Panel title="Временная шкала событий" bodyClassName="">
            {timeline.error && <ErrorBox error={timeline.error} />}
            {timeline.data && timeline.data.items.length === 0 && <Empty>Связанных событий нет</Empty>}
            {timeline.data && timeline.data.items.length > 0 && (
              <ol className="relative max-h-[460px] overflow-auto py-2 pr-3 pl-8">
                <span className="absolute top-0 bottom-0 left-[18px] w-px bg-line" />
                {timeline.data.items.map((e) => (
                  <li key={e.id} className="relative cursor-pointer py-1.5 hover:bg-panel-2" onClick={() => setOpenEvent(e.id)}>
                    <span className="absolute top-[11px] -left-[17px]"><SeverityDot severity={e.severity} /></span>
                    <div className="flex flex-wrap items-baseline gap-x-3 gap-y-0.5 text-[12.5px]">
                      <span className="mono text-muted">{formatDateTime(e.timestamp)}</span>
                      <span className="font-medium text-ink-strong">{e.eventType}</span>
                      <span className="mono">{e.sourceIp ?? '—'}</span>
                      <span>{e.username ?? ''}</span>
                      <span className="truncate text-muted">{e.message}</span>
                    </div>
                  </li>
                ))}
              </ol>
            )}
            {timeline.data && <Pagination page={timelinePage} pageSize={100} total={timeline.data.totalCount} onPage={setTimelinePage} />}
          </Panel>
        </div>

        <div className="flex min-w-0 flex-col gap-3">
          <Panel title="Комментарии">
            <div className="mb-3 flex max-h-[360px] flex-col gap-2 overflow-auto">
              {comments.data?.length === 0 && <div className="text-xs text-faint">Комментариев пока нет</div>}
              {comments.data?.map((c) => (
                <div key={c.id} className="rounded-md border border-line bg-panel-2 px-3 py-2">
                  <div className="mb-1 flex justify-between gap-2 text-[11px] text-muted">
                    <span className="text-ink">{c.authorName}</span>
                    <span>{formatDateTime(c.createdAt)}</span>
                  </div>
                  <div className="text-[12.5px] whitespace-pre-wrap">{c.body}</div>
                </div>
              ))}
            </div>
            <textarea className="field mb-2 w-full" rows={3} placeholder="Ход расследования, принятые меры…" value={comment} onChange={(e) => setComment(e.target.value)} />
            <div className="flex justify-end">
              <button className="btn btn-primary" disabled={!comment.trim() || addComment.isPending} onClick={() => addComment.mutate()}>Добавить</button>
            </div>
          </Panel>
          <Panel title="История обработки">
            <ol className="flex flex-col gap-2.5">
              {history.data?.map((h) => (
                <li key={h.id} className="border-l-2 border-line pl-3">
                  <div className="text-[12.5px] text-ink">{HISTORY_LABEL[h.action]}</div>
                  <div className="text-xs text-muted">
                    {h.oldValue && h.action !== 'CommentAdded' && <span>{statusLabel(h.oldValue)} → </span>}
                    {h.newValue && <span className="text-ink/80">{h.action === 'StatusChanged' ? statusLabel(h.newValue) : h.newValue}</span>}
                  </div>
                  <div className="text-[11px] text-faint">{h.actorName ?? 'система'} · {formatDateTime(h.createdAt)}</div>
                </li>
              ))}
            </ol>
          </Panel>
        </div>
      </div>

      {openAlert && <AlertDetailsDrawer id={openAlert} onClose={() => setOpenAlert(null)} />}
      {openEvent && <EventDetailsDrawer id={openEvent} onClose={() => setOpenEvent(null)} />}
      {linking && <LinkAlertsModal incidentId={id} onClose={() => setLinking(false)} onLinked={refresh} />}
    </>
  )
}

function LinkAlertsModal({ incidentId, onClose, onLinked }: { incidentId: string; onClose: () => void; onLinked: () => void }) {
  const [selected, setSelected] = useState<string[]>([])
  const candidates = useQuery({
    queryKey: ['alerts', 'linkable'],
    queryFn: () => api.get<PagedResult<AlertListItem>>('/api/alerts', { status: ['New', 'Acknowledged'], pageSize: 100 })
  })
  const link = useMutation({
    mutationFn: () => api.post(`/api/incidents/${incidentId}/alerts`, { alertIds: selected }),
    onSuccess: () => {
      onLinked()
      onClose()
    }
  })
  const items = (candidates.data?.items ?? []).filter((a) => !a.incidentId)
  return (
    <Modal
      title="Привязать предупреждения"
      onClose={onClose}
      width={680}
      footer={
        <>
          <button className="btn" onClick={onClose}>Отмена</button>
          <button className="btn btn-primary" disabled={selected.length === 0 || link.isPending} onClick={() => link.mutate()}>
            Привязать ({selected.length})
          </button>
        </>
      }
    >
      {candidates.isLoading && <Spinner />}
      {items.length === 0 && !candidates.isLoading && <Empty>Нет свободных предупреждений</Empty>}
      <ul className="flex flex-col gap-1">
        {items.map((a) => (
          <li key={a.id}>
            <label className="flex cursor-pointer items-center gap-2 rounded px-2 py-1 hover:bg-panel-2">
              <input
                type="checkbox"
                checked={selected.includes(a.id)}
                onChange={() => setSelected((s) => (s.includes(a.id) ? s.filter((x) => x !== a.id) : [...s, a.id]))}
              />
              <SeverityBadge severity={a.severity} />
              <span className="truncate text-[12.5px]">{a.title}</span>
              <span className="ml-auto text-xs whitespace-nowrap text-muted">{formatDateTime(a.detectedAt)}</span>
            </label>
          </li>
        ))}
      </ul>
      {link.error && <ErrorBox error={link.error} />}
    </Modal>
  )
}
