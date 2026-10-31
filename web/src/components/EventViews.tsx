import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { api } from '../api/client'
import type { EventDetails, EventListItem } from '../api/types'
import { formatDateTime } from '../lib/format'
import { Drawer, ErrorBox, JsonView, KeyValue, SeverityBadge, Spinner } from './ui'

export function EventsTable({ items, onOpen, compact = false }: { items: EventListItem[]; onOpen?: (id: string) => void; compact?: boolean }) {
  return (
    <table className="table">
      <thead>
        <tr>
          <th className="w-[150px]">Время</th>
          <th className="w-[96px]">Критичность</th>
          <th>Тип</th>
          <th>Source IP</th>
          {!compact && <th>Target IP</th>}
          <th>Пользователь</th>
          {!compact && <th>Хост</th>}
          {!compact && <th>Источник</th>}
          <th>Сообщение</th>
        </tr>
      </thead>
      <tbody>
        {items.map((e) => (
          <tr key={e.id} className={onOpen ? 'clickable' : ''} onClick={() => onOpen?.(e.id)}>
            <td className="tabular-nums whitespace-nowrap text-muted">{formatDateTime(e.timestamp)}</td>
            <td><SeverityBadge severity={e.severity} /></td>
            <td className="whitespace-nowrap">{e.eventType}</td>
            <td className="mono whitespace-nowrap">{e.sourceIp ?? '—'}</td>
            {!compact && <td className="mono whitespace-nowrap text-muted">{e.targetIp ?? '—'}</td>}
            <td className="whitespace-nowrap">{e.username ?? '—'}</td>
            {!compact && <td className="whitespace-nowrap text-muted">{e.hostname ?? '—'}</td>}
            {!compact && (
              <td className="whitespace-nowrap text-muted">
                {e.sourceName}
                {e.isDemo && <span className="ml-1.5 rounded bg-panel-3 px-1 text-[10px] text-faint">demo</span>}
              </td>
            )}
            <td className="max-w-[360px] truncate text-muted">{e.message ?? ''}</td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}

export function EventDetailsDrawer({ id, onClose }: { id: string; onClose: () => void }) {
  const query = useQuery({ queryKey: ['event', id], queryFn: () => api.get<EventDetails>(`/api/events/${id}`) })
  const e = query.data
  return (
    <Drawer title={e ? `${e.eventType} · ${formatDateTime(e.timestamp)}` : 'Событие'} onClose={onClose} width={680}>
      {query.isLoading && <Spinner />}
      {query.error && <ErrorBox error={query.error} />}
      {e && (
        <div className="flex flex-col gap-5">
          <KeyValue
            items={[
              ['ID', <span className="mono">{e.id}</span>],
              ['Критичность', <SeverityBadge severity={e.severity} />],
              ['Время события', formatDateTime(e.timestamp)],
              ['Время приёма', formatDateTime(e.receivedAt)],
              ['Источник', `${e.sourceName} (${e.sourceKind})${e.isDemo ? ' · demo' : ''}`],
              ['Source IP', <span className="mono">{e.sourceIp ?? '—'}</span>],
              ['Target IP', <span className="mono">{e.targetIp ?? '—'}</span>],
              ['Пользователь', e.username],
              ['Хост', e.hostname],
              ['Сообщение', e.message],
              ['Correlation ID', <span className="mono">{e.correlationId ?? '—'}</span>],
              [
                'Предупреждения',
                e.alertIds.length === 0
                  ? 'не связано'
                  : e.alertIds.map((alertId) => (
                      <Link key={alertId} to={`/alerts?open=${alertId}`} className="mr-2 text-accent hover:underline" onClick={onClose}>
                        {alertId.slice(0, 8)}
                      </Link>
                    ))
              ]
            ]}
          />
          <div>
            <div className="panel-title mb-2">Исходные данные (RawData, JSON)</div>
            <JsonView value={e.rawData} />
          </div>
        </div>
      )}
    </Drawer>
  )
}
