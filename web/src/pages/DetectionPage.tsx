import { useState } from 'react'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import type { AlertListItem, PagedResult, Rule } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { AlertDetailsDrawer } from '../components/AlertViews'
import { useToasts } from '../components/Toasts'
import { Empty, ErrorBox, PageHeader, Pagination, Panel, SeverityBadge, Spinner, StatusBadge, Toggle, errorText } from '../components/ui'
import { formatDateTime, formatNumber, formatRelative } from '../lib/format'

export function describeRule(rule: Rule): string {
  const window = `${rule.windowSeconds} с`
  const group = rule.groupBy.join(' + ')
  const filter = rule.parameters.field && rule.parameters.values?.length ? `, где ${rule.parameters.field} ∈ {${rule.parameters.values.join(', ')}}` : ''
  switch (rule.ruleType) {
    case 'Threshold':
      return `≥ ${rule.threshold} событий ${rule.eventType}${filter} за ${window}, группировка по ${group}`
    case 'DistinctCount':
      return `≥ ${rule.threshold} различных ${rule.parameters.distinctField} в событиях ${rule.eventType} за ${window}, группировка по ${group}`
    case 'Sequence':
      return `≥ ${rule.threshold} × ${rule.eventType}, затем ${rule.parameters.thenEventType} в течение ${window}, группировка по ${group}`
    case 'SingleEvent':
      return `Событие ${rule.eventType}${filter}, группировка по ${group}`
  }
}

export function DetectionPage() {
  const { isAdmin } = useAuth()
  const toasts = useToasts()
  const queryClient = useQueryClient()
  const [ruleId, setRuleId] = useState<string | null>(null)
  const [page, setPage] = useState(1)
  const [openAlert, setOpenAlert] = useState<string | null>(null)

  const rules = useQuery({ queryKey: ['rules'], queryFn: () => api.get<Rule[]>('/api/rules'), refetchInterval: 30_000 })
  const history = useQuery({
    queryKey: ['alerts', 'by-rule', ruleId, page],
    queryFn: () => api.get<PagedResult<AlertListItem>>('/api/alerts', { ruleId: ruleId ?? undefined, page, pageSize: 25 }),
    placeholderData: keepPreviousData
  })
  const toggle = useMutation({
    mutationFn: ({ id, isEnabled }: { id: string; isEnabled: boolean }) => api.patch<Rule>(`/api/rules/${id}/enabled`, { isEnabled }),
    onSuccess: (rule) => {
      toasts.success(`Правило «${rule.name}» ${rule.isEnabled ? 'включено' : 'отключено'}`)
      queryClient.invalidateQueries({ queryKey: ['rules'] })
    },
    onError: (error) => toasts.error(errorText(error))
  })

  const selected = rules.data?.find((r) => r.id === ruleId)

  return (
    <>
      <PageHeader title="Threat Detection" subtitle="Правила корреляции, срабатывания и их причины" />
      <Panel title="Правила обнаружения" className="mb-4" bodyClassName="">
        {rules.isLoading && <Spinner />}
        {rules.error && <ErrorBox error={rules.error} />}
        {rules.data && (
          <table className="table">
            <thead>
              <tr>
                <th className="w-[70px]">Активно</th>
                <th>Правило</th>
                <th>Логика</th>
                <th>Критичность</th>
                <th className="text-right">За 24 ч</th>
                <th className="text-right">Всего</th>
                <th>Последнее</th>
              </tr>
            </thead>
            <tbody>
              {rules.data.map((rule) => (
                <tr
                  key={rule.id}
                  className={`clickable ${rule.id === ruleId ? 'bg-panel-3' : ''} ${rule.isEnabled ? '' : 'opacity-55'}`}
                  onClick={() => {
                    setRuleId(rule.id === ruleId ? null : rule.id)
                    setPage(1)
                  }}
                >
                  <td>
                    <Toggle
                      checked={rule.isEnabled}
                      disabled={!isAdmin || toggle.isPending}
                      onChange={(isEnabled) => toggle.mutate({ id: rule.id, isEnabled })}
                    />
                  </td>
                  <td>
                    <div className="font-medium text-ink-strong">{rule.name}</div>
                    <div className="mono text-[11px] text-faint">{rule.code} · {rule.ruleType}</div>
                  </td>
                  <td className="max-w-[520px] text-muted">{describeRule(rule)}</td>
                  <td><SeverityBadge severity={rule.severity} /></td>
                  <td className="text-right tabular-nums">{formatNumber(rule.alerts24h)}</td>
                  <td className="text-right tabular-nums">{formatNumber(rule.alertsTotal)}</td>
                  <td className="whitespace-nowrap text-muted">{formatRelative(rule.lastDetectedAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        {!isAdmin && <div className="border-t border-line px-4 py-2 text-xs text-faint">Включать и отключать правила может только администратор</div>}
      </Panel>

      <Panel
        title={selected ? `История срабатываний: ${selected.name}` : 'История срабатываний: все правила'}
        actions={selected && <button className="btn btn-ghost h-7 text-xs" onClick={() => setRuleId(null)}>Показать все</button>}
        bodyClassName=""
      >
        {history.error && <ErrorBox error={history.error} />}
        {history.data && history.data.items.length === 0 && <Empty>Срабатываний нет</Empty>}
        {history.data && history.data.items.length > 0 && (
          <table className="table">
            <thead>
              <tr>
                <th>Обнаружено</th>
                <th>Критичность</th>
                <th>Предупреждение</th>
                <th>Правило</th>
                <th className="text-right">Событий</th>
                <th>Период</th>
                <th>Статус</th>
              </tr>
            </thead>
            <tbody>
              {history.data.items.map((a) => (
                <tr key={a.id} className="clickable" onClick={() => setOpenAlert(a.id)}>
                  <td className="tabular-nums whitespace-nowrap text-muted">{formatDateTime(a.detectedAt)}</td>
                  <td><SeverityBadge severity={a.severity} /></td>
                  <td className="max-w-[380px] truncate">{a.title}</td>
                  <td className="mono text-[11px] text-muted">{a.ruleCode}</td>
                  <td className="text-right tabular-nums">{formatNumber(a.eventCount)}</td>
                  <td className="tabular-nums whitespace-nowrap text-xs text-muted">
                    {formatDateTime(a.firstEventAt).slice(-8)} – {formatDateTime(a.lastEventAt).slice(-8)}
                  </td>
                  <td><StatusBadge status={a.status} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        {history.data && <Pagination page={page} pageSize={25} total={history.data.totalCount} onPage={setPage} />}
      </Panel>
      {openAlert && <AlertDetailsDrawer id={openAlert} onClose={() => setOpenAlert(null)} />}
    </>
  )
}
