import { useState, type FormEvent } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import { SEVERITIES, type EventListItem, type PagedResult, type Severity, type Source } from '../api/types'
import { EventDetailsDrawer, EventsTable } from '../components/EventViews'
import { ChipToggle, Empty, ErrorBox, PageHeader, Pagination, SeverityDot, SortHeader, Spinner } from '../components/ui'
import { fromLocalInput } from '../lib/format'

interface Filters {
  from: string
  to: string
  severity: Severity[]
  sourceId: string
  eventType: string
  sourceIp: string
  username: string
  q: string
}

const EMPTY: Filters = { from: '', to: '', severity: [], sourceId: '', eventType: '', sourceIp: '', username: '', q: '' }

export function EventsPage() {
  const [draft, setDraft] = useState<Filters>(EMPTY)
  const [filters, setFilters] = useState<Filters>(EMPTY)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(50)
  const [sort, setSort] = useState('-timestamp')
  const [openId, setOpenId] = useState<string | null>(null)

  const sources = useQuery({ queryKey: ['sources'], queryFn: () => api.get<Source[]>('/api/sources') })
  const events = useQuery({
    queryKey: ['events', filters, page, pageSize, sort],
    queryFn: () =>
      api.get<PagedResult<EventListItem>>('/api/events', {
        from: fromLocalInput(filters.from),
        to: fromLocalInput(filters.to),
        severity: filters.severity,
        sourceId: filters.sourceId ? [filters.sourceId] : undefined,
        eventType: filters.eventType,
        sourceIp: filters.sourceIp,
        username: filters.username,
        q: filters.q,
        page,
        pageSize,
        sort: sort === '-timestamp' ? undefined : sort
      }),
    placeholderData: keepPreviousData
  })

  const apply = (e?: FormEvent) => {
    e?.preventDefault()
    setFilters(draft)
    setPage(1)
  }

  const reset = () => {
    setDraft(EMPTY)
    setFilters(EMPTY)
    setPage(1)
  }

  const set = <K extends keyof Filters>(key: K, value: Filters[K]) => setDraft((d) => ({ ...d, [key]: value }))

  return (
    <>
      <PageHeader title="Event Explorer" subtitle="Централизованный журнал событий безопасности с серверной пагинацией" />
      <form onSubmit={apply} className="panel mb-3 grid grid-cols-2 gap-3 p-3 md:grid-cols-4 xl:grid-cols-8">
        <div className="col-span-2">
          <label className="label">Поиск</label>
          <input className="field w-full" placeholder="сообщение, пользователь, хост, тип" value={draft.q} onChange={(e) => set('q', e.target.value)} />
        </div>
        <div>
          <label className="label">С</label>
          <input type="datetime-local" className="field w-full" value={draft.from} onChange={(e) => set('from', e.target.value)} />
        </div>
        <div>
          <label className="label">По</label>
          <input type="datetime-local" className="field w-full" value={draft.to} onChange={(e) => set('to', e.target.value)} />
        </div>
        <div>
          <label className="label">IP</label>
          <input className="field mono w-full" placeholder="10.0.0.5" value={draft.sourceIp} onChange={(e) => set('sourceIp', e.target.value)} />
        </div>
        <div>
          <label className="label">Пользователь</label>
          <input className="field w-full" value={draft.username} onChange={(e) => set('username', e.target.value)} />
        </div>
        <div>
          <label className="label">Тип события</label>
          <input className="field w-full" placeholder="LoginFailed" value={draft.eventType} onChange={(e) => set('eventType', e.target.value)} />
        </div>
        <div>
          <label className="label">Источник</label>
          <select className="field w-full" value={draft.sourceId} onChange={(e) => set('sourceId', e.target.value)}>
            <option value="">Все</option>
            {sources.data?.map((s) => (
              <option key={s.id} value={s.id}>{s.name}</option>
            ))}
          </select>
        </div>
        <div className="col-span-2 md:col-span-3 xl:col-span-5">
          <label className="label">Критичность</label>
          <ChipToggle
            options={SEVERITIES}
            value={draft.severity}
            onChange={(v) => set('severity', v)}
            render={(s) => (
              <span className="flex items-center gap-1.5"><SeverityDot severity={s} />{s}</span>
            )}
          />
        </div>
        <div className="col-span-2 flex items-end justify-end gap-2 md:col-span-1 xl:col-span-3">
          <button type="button" className="btn btn-ghost" onClick={reset}>Сбросить</button>
          <button className="btn btn-primary">Применить</button>
        </div>
      </form>

      <section className="panel flex flex-col">
        <div className="flex h-10 items-center justify-between border-b border-line px-3 text-xs text-muted">
          <div className="flex items-center gap-3">
            <span>Сортировка:</span>
            <SortHeader label="Время" field="timestamp" sort={sort} onSort={(s) => { setSort(s); setPage(1) }} />
            <SortHeader label="Критичность" field="severity" sort={sort} onSort={(s) => { setSort(s); setPage(1) }} />
          </div>
          <div className="flex items-center gap-2">
            {events.isFetching && <span className="text-faint">обновление…</span>}
            <select className="field h-7" value={pageSize} onChange={(e) => { setPageSize(Number(e.target.value)); setPage(1) }}>
              {[25, 50, 100, 200].map((n) => <option key={n} value={n}>{n} / стр.</option>)}
            </select>
          </div>
        </div>
        <div className="max-h-[calc(100vh-330px)] min-h-[200px] overflow-auto">
          {events.isLoading && <Spinner />}
          {events.error && <ErrorBox error={events.error} />}
          {events.data && events.data.items.length === 0 && <Empty>События не найдены</Empty>}
          {events.data && events.data.items.length > 0 && <EventsTable items={events.data.items} onOpen={setOpenId} />}
        </div>
        {events.data && <Pagination page={page} pageSize={pageSize} total={events.data.totalCount} onPage={setPage} />}
      </section>
      {openId && <EventDetailsDrawer id={openId} onClose={() => setOpenId(null)} />}
    </>
  )
}
