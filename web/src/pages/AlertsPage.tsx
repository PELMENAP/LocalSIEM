import { useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import { ALERT_STATUSES, SEVERITIES, type AlertListItem, type AlertStatus, type PagedResult, type Severity } from '../api/types'
import { AlertDetailsDrawer, CreateIncidentModal, useAlertStatus } from '../components/AlertViews'
import { ChipToggle, Empty, ErrorBox, PageHeader, Pagination, SeverityBadge, SeverityDot, SortHeader, Spinner, StatusBadge, statusLabel } from '../components/ui'
import { formatDateTime, formatNumber } from '../lib/format'

export function AlertsPage() {
  const [params, setParams] = useSearchParams()
  const openId = params.get('open')
  const [status, setStatus] = useState<AlertStatus[]>(['New', 'Acknowledged'])
  const [severity, setSeverity] = useState<Severity[]>([])
  const [q, setQ] = useState('')
  const [search, setSearch] = useState('')
  const [sort, setSort] = useState('-detectedAt')
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState<Map<string, AlertListItem>>(new Map())
  const [incidentFor, setIncidentFor] = useState<Pick<AlertListItem, 'id' | 'title' | 'severity'>[] | null>(null)
  const changeStatus = useAlertStatus()

  const alerts = useQuery({
    queryKey: ['alerts', status, severity, search, sort, page],
    queryFn: () =>
      api.get<PagedResult<AlertListItem>>('/api/alerts', {
        status,
        severity,
        q: search,
        sort: sort === '-detectedAt' ? undefined : sort,
        page,
        pageSize: 50
      }),
    placeholderData: keepPreviousData
  })

  const toggle = (alert: AlertListItem) =>
    setSelected((prev) => {
      const next = new Map(prev)
      if (next.has(alert.id)) next.delete(alert.id)
      else next.set(alert.id, alert)
      return next
    })

  const open = (id: string | null) => {
    const next = new URLSearchParams(params)
    if (id) next.set('open', id)
    else next.delete('open')
    setParams(next, { replace: true })
  }

  const selectable = (a: AlertListItem) => !a.incidentId
  const pageItems = alerts.data?.items ?? []
  const allSelected = pageItems.filter(selectable).length > 0 && pageItems.filter(selectable).every((a) => selected.has(a.id))

  return (
    <>
      <PageHeader
        title="Alerts"
        subtitle="Предупреждения, созданные Detection Engine"
        actions={
          selected.size > 0 && (
            <>
              <span className="text-xs text-muted">Выбрано: {selected.size}</span>
              <button className="btn" onClick={() => setSelected(new Map())}>Снять выбор</button>
              <button className="btn btn-primary" onClick={() => setIncidentFor([...selected.values()])}>Создать инцидент</button>
            </>
          )
        }
      />
      <div className="panel mb-3 flex flex-wrap items-end gap-4 p-3">
        <div>
          <div className="label">Статус</div>
          <ChipToggle options={ALERT_STATUSES} value={status} onChange={(v) => { setStatus(v); setPage(1) }} render={statusLabel} />
        </div>
        <div>
          <div className="label">Критичность</div>
          <ChipToggle
            options={SEVERITIES}
            value={severity}
            onChange={(v) => { setSeverity(v); setPage(1) }}
            render={(s) => <span className="flex items-center gap-1.5"><SeverityDot severity={s} />{s}</span>}
          />
        </div>
        <form
          className="ml-auto flex items-end gap-2"
          onSubmit={(e) => {
            e.preventDefault()
            setSearch(q)
            setPage(1)
          }}
        >
          <div>
            <div className="label">Поиск</div>
            <input className="field w-64" placeholder="IP, пользователь, правило" value={q} onChange={(e) => setQ(e.target.value)} />
          </div>
          <button className="btn">Найти</button>
        </form>
      </div>

      <section className="panel flex flex-col">
        <div className="max-h-[calc(100vh-290px)] min-h-[200px] overflow-auto">
          {alerts.isLoading && <Spinner />}
          {alerts.error && <ErrorBox error={alerts.error} />}
          {alerts.data && pageItems.length === 0 && <Empty>Предупреждений нет</Empty>}
          {pageItems.length > 0 && (
            <table className="table">
              <thead>
                <tr>
                  <th className="w-8">
                    <input
                      type="checkbox"
                      checked={allSelected}
                      onChange={() =>
                        setSelected((prev) => {
                          const next = new Map(prev)
                          pageItems.filter(selectable).forEach((a) => (allSelected ? next.delete(a.id) : next.set(a.id, a)))
                          return next
                        })
                      }
                    />
                  </th>
                  <th><SortHeader label="Обнаружено" field="detectedAt" sort={sort} onSort={setSort} /></th>
                  <th><SortHeader label="Критичность" field="severity" sort={sort} onSort={setSort} /></th>
                  <th>Предупреждение</th>
                  <th>Правило</th>
                  <th className="text-right"><SortHeader label="Событий" field="eventCount" sort={sort} onSort={setSort} /></th>
                  <th><SortHeader label="Статус" field="status" sort={sort} onSort={setSort} defaultDesc={false} /></th>
                  <th>Инцидент</th>
                  <th className="w-[150px]" />
                </tr>
              </thead>
              <tbody>
                {pageItems.map((a) => (
                  <tr key={a.id} className="clickable" onClick={() => open(a.id)}>
                    <td onClick={(e) => e.stopPropagation()}>
                      <input type="checkbox" disabled={!selectable(a)} checked={selected.has(a.id)} onChange={() => toggle(a)} />
                    </td>
                    <td className="tabular-nums whitespace-nowrap text-muted">{formatDateTime(a.detectedAt)}</td>
                    <td><SeverityBadge severity={a.severity} /></td>
                    <td className="max-w-[420px] truncate text-ink-strong">{a.title}</td>
                    <td className="whitespace-nowrap text-muted">{a.ruleName}</td>
                    <td className="text-right tabular-nums">{formatNumber(a.eventCount)}</td>
                    <td><StatusBadge status={a.status} /></td>
                    <td onClick={(e) => e.stopPropagation()}>
                      {a.incidentId ? <Link to={`/incidents/${a.incidentId}`} className="text-accent hover:underline">INC-{a.incidentNumber}</Link> : <span className="text-faint">—</span>}
                    </td>
                    <td className="text-right" onClick={(e) => e.stopPropagation()}>
                      {a.status === 'New' && (
                        <button className="btn h-6 px-2 text-xs" onClick={() => changeStatus.mutate({ id: a.id, status: 'Acknowledged', version: a.version })}>В работу</button>
                      )}
                      {a.status !== 'Resolved' && (
                        <button className="btn btn-ghost ml-1 h-6 px-2 text-xs" onClick={() => changeStatus.mutate({ id: a.id, status: 'Resolved', version: a.version })}>Решено</button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
        {alerts.data && <Pagination page={page} pageSize={50} total={alerts.data.totalCount} onPage={setPage} />}
      </section>

      {openId && (
        <AlertDetailsDrawer
          id={openId}
          onClose={() => open(null)}
          onCreateIncident={(a) => setIncidentFor([{ id: a.id, title: a.title, severity: a.severity }])}
        />
      )}
      {incidentFor && <CreateIncidentModal alerts={incidentFor} onClose={() => setIncidentFor(null)} />}
    </>
  )
}
