import { useEffect, type ReactNode } from 'react'
import type { AlertStatus, IncidentStatus, Severity, SourceStatus } from '../api/types'
import { ApiError } from '../api/client'
import { SEVERITY_COLOR, formatNumber } from '../lib/format'

export function SeverityBadge({ severity }: { severity: Severity }) {
  const color = SEVERITY_COLOR[severity]
  return (
    <span
      className="inline-flex h-5 min-w-[64px] items-center justify-center gap-1.5 rounded px-1.5 text-[11px] font-semibold tracking-wide"
      style={{ color, background: `${color}1f`, border: `1px solid ${color}55` }}
    >
      <span className="h-1.5 w-1.5 rounded-full" style={{ background: color }} />
      {severity}
    </span>
  )
}

export function SeverityDot({ severity }: { severity: Severity }) {
  return <span className="inline-block h-2 w-2 shrink-0 rounded-full" style={{ background: SEVERITY_COLOR[severity] }} />
}

const STATUS_STYLE: Record<string, string> = {
  New: 'text-sky-300 border-sky-400/40 bg-sky-400/10',
  Acknowledged: 'text-violet-300 border-violet-400/40 bg-violet-400/10',
  Resolved: 'text-emerald-300 border-emerald-400/40 bg-emerald-400/10',
  Open: 'text-sky-300 border-sky-400/40 bg-sky-400/10',
  Investigating: 'text-amber-300 border-amber-400/40 bg-amber-400/10',
  Closed: 'text-zinc-400 border-zinc-500/40 bg-zinc-500/10',
  Online: 'text-emerald-300 border-emerald-400/40 bg-emerald-400/10',
  Idle: 'text-zinc-300 border-zinc-500/40 bg-zinc-500/10',
  Disabled: 'text-zinc-500 border-zinc-600/40 bg-zinc-600/10'
}

const STATUS_LABEL: Record<string, string> = {
  New: 'Новое',
  Acknowledged: 'В работе',
  Resolved: 'Решено',
  Open: 'Открыт',
  Investigating: 'Расследование',
  Closed: 'Закрыт',
  Online: 'Online',
  Idle: 'Нет данных',
  Disabled: 'Отключён'
}

export function StatusBadge({ status }: { status: AlertStatus | IncidentStatus | SourceStatus }) {
  return (
    <span className={`inline-flex h-5 items-center rounded border px-1.5 text-[11px] font-medium ${STATUS_STYLE[status] ?? ''}`}>
      {STATUS_LABEL[status] ?? status}
    </span>
  )
}

export const statusLabel = (status: string) => STATUS_LABEL[status] ?? status

export function Stat({ label, value, hint, tone }: { label: string; value: number | string; hint?: ReactNode; tone?: string }) {
  return (
    <div className="panel flex flex-col gap-1 px-4 py-3">
      <div className="panel-title">{label}</div>
      <div className="text-[26px] leading-none font-semibold tabular-nums text-ink-strong" style={tone ? { color: tone } : undefined}>
        {typeof value === 'number' ? formatNumber(value) : value}
      </div>
      {hint && <div className="text-xs text-muted">{hint}</div>}
    </div>
  )
}

export function Panel({ title, actions, children, className = '', bodyClassName = 'p-4' }: {
  title?: ReactNode
  actions?: ReactNode
  children: ReactNode
  className?: string
  bodyClassName?: string
}) {
  return (
    <section className={`panel flex min-h-0 flex-col ${className}`}>
      {(title || actions) && (
        <header className="flex h-10 shrink-0 items-center justify-between gap-2 border-b border-line px-4">
          <div className="panel-title">{title}</div>
          <div className="flex items-center gap-2">{actions}</div>
        </header>
      )}
      <div className={`min-h-0 flex-1 ${bodyClassName}`}>{children}</div>
    </section>
  )
}

export function PageHeader({ title, subtitle, actions }: { title: string; subtitle?: ReactNode; actions?: ReactNode }) {
  return (
    <div className="mb-4 flex flex-wrap items-end justify-between gap-3">
      <div>
        <h1 className="text-lg font-semibold text-ink-strong">{title}</h1>
        {subtitle && <div className="mt-0.5 text-xs text-muted">{subtitle}</div>}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
    </div>
  )
}

export function Pagination({ page, pageSize, total, onPage }: { page: number; pageSize: number; total: number; onPage: (page: number) => void }) {
  const pages = Math.max(1, Math.ceil(total / pageSize))
  const from = total === 0 ? 0 : (page - 1) * pageSize + 1
  const to = Math.min(total, page * pageSize)
  return (
    <div className="flex h-10 shrink-0 items-center justify-between border-t border-line px-3 text-xs text-muted">
      <span className="tabular-nums">
        {formatNumber(from)}–{formatNumber(to)} из {formatNumber(total)}
      </span>
      <div className="flex items-center gap-1">
        <button className="btn btn-ghost h-7 px-2" disabled={page <= 1} onClick={() => onPage(1)}>«</button>
        <button className="btn btn-ghost h-7 px-2" disabled={page <= 1} onClick={() => onPage(page - 1)}>‹</button>
        <span className="px-2 tabular-nums text-ink">{page} / {pages}</span>
        <button className="btn btn-ghost h-7 px-2" disabled={page >= pages} onClick={() => onPage(page + 1)}>›</button>
        <button className="btn btn-ghost h-7 px-2" disabled={page >= pages} onClick={() => onPage(pages)}>»</button>
      </div>
    </div>
  )
}

export function Spinner({ label = 'Загрузка…' }: { label?: string }) {
  return (
    <div className="flex items-center justify-center gap-2 py-10 text-xs text-muted">
      <span className="h-3.5 w-3.5 animate-spin rounded-full border-2 border-line-strong border-t-accent" />
      {label}
    </div>
  )
}

export function Empty({ children }: { children: ReactNode }) {
  return <div className="py-10 text-center text-xs text-muted">{children}</div>
}

export function ErrorBox({ error }: { error: unknown }) {
  const message = error instanceof ApiError ? error.details : error instanceof Error ? error.message : 'Неизвестная ошибка'
  return <div className="m-3 rounded-md border border-sev-critical/40 bg-sev-critical/10 px-3 py-2 text-xs text-red-300">{message}</div>
}

export function errorText(error: unknown): string {
  return error instanceof ApiError ? error.details : error instanceof Error ? error.message : 'Неизвестная ошибка'
}

export function Modal({ title, onClose, children, footer, width = 520 }: {
  title: string
  onClose: () => void
  children: ReactNode
  footer?: ReactNode
  width?: number
}) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])
  return (
    <div className="fixed inset-0 z-40 flex items-start justify-center bg-black/60 p-4 pt-[10vh]" onMouseDown={onClose}>
      <div className="panel max-h-[80vh] w-full overflow-hidden shadow-2xl" style={{ maxWidth: width }} onMouseDown={(e) => e.stopPropagation()}>
        <div className="flex h-11 items-center justify-between border-b border-line px-4">
          <div className="text-sm font-semibold text-ink-strong">{title}</div>
          <button className="btn btn-ghost h-7 px-2" onClick={onClose}>✕</button>
        </div>
        <div className="max-h-[calc(80vh-96px)] overflow-auto p-4">{children}</div>
        {footer && <div className="flex justify-end gap-2 border-t border-line px-4 py-2.5">{footer}</div>}
      </div>
    </div>
  )
}

export function Drawer({ title, onClose, children, actions, width = 640 }: {
  title: ReactNode
  onClose: () => void
  children: ReactNode
  actions?: ReactNode
  width?: number
}) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])
  return (
    <div className="fixed inset-0 z-30 flex justify-end bg-black/40" onMouseDown={onClose}>
      <aside
        className="flex h-full w-full flex-col border-l border-line bg-panel shadow-2xl"
        style={{ maxWidth: width }}
        onMouseDown={(e) => e.stopPropagation()}
      >
        <div className="flex min-h-12 items-center justify-between gap-3 border-b border-line px-4 py-2">
          <div className="min-w-0 text-sm font-semibold text-ink-strong">{title}</div>
          <div className="flex shrink-0 items-center gap-2">
            {actions}
            <button className="btn btn-ghost h-7 px-2" onClick={onClose}>✕</button>
          </div>
        </div>
        <div className="min-h-0 flex-1 overflow-auto p-4">{children}</div>
      </aside>
    </div>
  )
}

export function KeyValue({ items }: { items: [string, ReactNode][] }) {
  return (
    <dl className="grid grid-cols-[150px_1fr] gap-x-3 gap-y-1.5 text-[12.5px]">
      {items.map(([key, value]) => (
        <div key={key} className="contents">
          <dt className="text-muted">{key}</dt>
          <dd className="min-w-0 break-words text-ink">{value ?? '—'}</dd>
        </div>
      ))}
    </dl>
  )
}

export function JsonView({ value }: { value: unknown }) {
  return (
    <pre className="mono max-h-[420px] overflow-auto rounded-md border border-line bg-bg p-3 text-[11.5px] leading-relaxed text-emerald-200/90">
      {JSON.stringify(value, null, 2)}
    </pre>
  )
}

export function ChipToggle<T extends string>({ options, value, onChange, render }: {
  options: readonly T[]
  value: T[]
  onChange: (value: T[]) => void
  render?: (option: T) => ReactNode
}) {
  return (
    <div className="flex flex-wrap gap-1">
      {options.map((option) => {
        const active = value.includes(option)
        return (
          <button
            key={option}
            type="button"
            className={`h-7 rounded border px-2 text-xs transition-colors ${
              active ? 'border-accent/70 bg-accent/15 text-ink-strong' : 'border-line bg-panel-2 text-muted hover:text-ink'
            }`}
            onClick={() => onChange(active ? value.filter((v) => v !== option) : [...value, option])}
          >
            {render ? render(option) : option}
          </button>
        )
      })}
    </div>
  )
}

export function Toggle({ checked, onChange, disabled }: { checked: boolean; onChange: (value: boolean) => void; disabled?: boolean }) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      disabled={disabled}
      onClick={(e) => {
        e.stopPropagation()
        onChange(!checked)
      }}
      className={`relative h-5 w-9 shrink-0 rounded-full border transition-colors disabled:cursor-not-allowed disabled:opacity-50 ${
        checked ? 'border-accent-strong bg-accent-strong/80' : 'border-line-strong bg-panel-3'
      }`}
    >
      <span className={`absolute top-0.5 h-3.5 w-3.5 rounded-full bg-white transition-all ${checked ? 'left-[18px]' : 'left-0.5'}`} />
    </button>
  )
}

export function SortHeader({ label, field, sort, onSort, defaultDesc = true }: {
  label: string
  field: string
  sort: string
  onSort: (sort: string) => void
  defaultDesc?: boolean
}) {
  const active = sort === field || sort === `-${field}`
  const desc = sort === `-${field}`
  return (
    <button
      type="button"
      className={`inline-flex items-center gap-1 uppercase ${active ? 'text-ink' : ''}`}
      onClick={() => onSort(active ? (desc ? field : `-${field}`) : defaultDesc ? `-${field}` : field)}
    >
      {label}
      <span className="text-[9px]">{active ? (desc ? '▼' : '▲') : '↕'}</span>
    </button>
  )
}
