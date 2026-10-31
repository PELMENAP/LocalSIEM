import { useState } from 'react'
import { useNavigate } from 'react-router'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import { INCIDENT_STATUSES, SEVERITIES, type IncidentListItem, type IncidentStatus, type PagedResult, type Severity } from '../api/types'
import { ChipToggle, Empty, ErrorBox, PageHeader, Pagination, SeverityBadge, SeverityDot, SortHeader, Spinner, StatusBadge, statusLabel } from '../components/ui'
import { formatDateTime, formatRelative } from '../lib/format'

export function IncidentsPage() {
  const navigate = useNavigate()
  const [status, setStatus] = useState<IncidentStatus[]>(['Open', 'Investigating'])
  const [severity, setSeverity] = useState<Severity[]>([])
  const [sort, setSort] = useState('-createdAt')
  const [page, setPage] = useState(1)

  const incidents = useQuery({
    queryKey: ['incidents', status, severity, sort, page],
    queryFn: () =>
      api.get<PagedResult<IncidentListItem>>('/api/incidents', {
        status,
        severity,
        sort: sort === '-createdAt' ? undefined : sort,
        page,
        pageSize: 50
      }),
    placeholderData: keepPreviousData
  })

  return (
    <>
      <PageHeader title="Incident Management" subtitle="Инциденты создаются из предупреждений на странице Alerts" />
      <div className="panel mb-3 flex flex-wrap gap-4 p-3">
        <div>
          <div className="label">Статус</div>
          <ChipToggle options={INCIDENT_STATUSES} value={status} onChange={(v) => { setStatus(v); setPage(1) }} render={statusLabel} />
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
      </div>
      <section className="panel flex flex-col">
        <div className="max-h-[calc(100vh-290px)] min-h-[200px] overflow-auto">
          {incidents.isLoading && <Spinner />}
          {incidents.error && <ErrorBox error={incidents.error} />}
          {incidents.data && incidents.data.items.length === 0 && <Empty>Инцидентов нет</Empty>}
          {incidents.data && incidents.data.items.length > 0 && (
            <table className="table">
              <thead>
                <tr>
                  <th className="w-[90px]">№</th>
                  <th><SortHeader label="Критичность" field="severity" sort={sort} onSort={setSort} /></th>
                  <th>Название</th>
                  <th><SortHeader label="Статус" field="status" sort={sort} onSort={setSort} defaultDesc={false} /></th>
                  <th>Ответственный</th>
                  <th className="text-right">Предупр.</th>
                  <th>Обнаружено</th>
                  <th><SortHeader label="Создан" field="createdAt" sort={sort} onSort={setSort} /></th>
                  <th>Обновлён</th>
                </tr>
              </thead>
              <tbody>
                {incidents.data.items.map((i) => (
                  <tr key={i.id} className="clickable" onClick={() => navigate(`/incidents/${i.id}`)}>
                    <td className="mono text-muted">INC-{i.number}</td>
                    <td><SeverityBadge severity={i.severity} /></td>
                    <td className="max-w-[420px] truncate text-ink-strong">{i.title}</td>
                    <td><StatusBadge status={i.status} /></td>
                    <td>{i.assigneeName ?? <span className="text-faint">не назначен</span>}</td>
                    <td className="text-right tabular-nums">{i.alertCount}</td>
                    <td className="tabular-nums whitespace-nowrap text-muted">{formatDateTime(i.detectedAt)}</td>
                    <td className="tabular-nums whitespace-nowrap text-muted">{formatDateTime(i.createdAt)}</td>
                    <td className="whitespace-nowrap text-muted">{formatRelative(i.updatedAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
        {incidents.data && <Pagination page={page} pageSize={50} total={incidents.data.totalCount} onPage={setPage} />}
      </section>
    </>
  )
}
