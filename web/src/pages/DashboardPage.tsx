import { useMemo, useState } from 'react'
import { Link } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import { SEVERITIES, type DashboardSummary, type Severity, type TimelinePoint, type TopIp } from '../api/types'
import { EChart, chartBase } from '../components/EChart'
import { Empty, ErrorBox, PageHeader, Panel, SeverityBadge, Spinner, Stat, StatusBadge } from '../components/ui'
import { SEVERITY_COLOR, formatNumber, formatRelative, formatTime } from '../lib/format'

const RANGES = {
  '1h': { label: '1 час', hours: 1, bucket: '1m', step: 60_000 },
  '6h': { label: '6 часов', hours: 6, bucket: '5m', step: 300_000 },
  '24h': { label: '24 часа', hours: 24, bucket: '15m', step: 900_000 }
} as const

type RangeKey = keyof typeof RANGES

export function DashboardPage() {
  const [range, setRange] = useState<RangeKey>('1h')
  const summary = useQuery({
    queryKey: ['dashboard', 'summary'],
    queryFn: () => api.get<DashboardSummary>('/api/dashboard/summary'),
    refetchInterval: 30_000
  })
  const timeline = useQuery({
    queryKey: ['dashboard', 'timeline', range],
    queryFn: () => {
      const to = new Date()
      const from = new Date(to.getTime() - RANGES[range].hours * 3600_000)
      return api.get<TimelinePoint[]>('/api/dashboard/timeline', { from: from.toISOString(), to: to.toISOString(), bucket: RANGES[range].bucket })
    },
    refetchInterval: 30_000
  })
  const topIps = useQuery({
    queryKey: ['dashboard', 'top-ips'],
    queryFn: () => api.get<TopIp[]>('/api/dashboard/top-ips', { hours: 24, limit: 8 }),
    refetchInterval: 30_000
  })

  const timelineOption = useMemo(() => buildTimeline(timeline.data ?? [], range), [timeline.data, range])
  const distributionOption = useMemo(() => buildDistribution(summary.data), [summary.data])
  const topIpOption = useMemo(() => buildTopIps(topIps.data ?? []), [topIps.data])

  if (summary.isLoading) return <Spinner />
  if (summary.error) return <ErrorBox error={summary.error} />
  const s = summary.data!
  const critical24 = s.severityDistribution24h.find((d) => d.severity === 'Critical')?.count ?? 0

  return (
    <>
      <PageHeader
        title="Dashboard"
        subtitle={`Обновлено ${formatTime(s.generatedAt)} · очередь анализа: ${formatNumber(s.detectionQueue)}`}
      />
      <div className="mb-4 grid grid-cols-2 gap-3 lg:grid-cols-5">
        <Stat label="Всего событий" value={s.totalEvents} />
        <Stat label="События за 24 ч" value={s.events24h} hint={critical24 > 0 ? <span className="text-red-300">{formatNumber(critical24)} critical</span> : 'без critical'} />
        <Stat label="Предупреждения" value={s.alertsTotal} hint={`${formatNumber(s.alertsNew)} новых · ${formatNumber(s.alertsAcknowledged)} в работе`} />
        <Stat label="Новые предупреждения" value={s.alertsNew} tone={s.alertsNew > 0 ? SEVERITY_COLOR.High : undefined} />
        <Stat label="Активные инциденты" value={s.activeIncidents} tone={s.activeIncidents > 0 ? SEVERITY_COLOR.Critical : undefined} />
      </div>

      <div className="mb-4 grid grid-cols-1 gap-3 xl:grid-cols-3">
        <Panel
          title="Динамика событий"
          className="xl:col-span-2"
          actions={
            <div className="flex gap-1">
              {(Object.keys(RANGES) as RangeKey[]).map((key) => (
                <button key={key} className={`btn h-6 px-2 text-xs ${range === key ? 'border-accent/60 text-ink-strong' : 'btn-ghost text-muted'}`} onClick={() => setRange(key)}>
                  {RANGES[key].label}
                </button>
              ))}
            </div>
          }
          bodyClassName="px-2 pt-2"
        >
          {timeline.error ? <ErrorBox error={timeline.error} /> : <EChart option={timelineOption} height={260} />}
        </Panel>
        <Panel title="Критичность за 24 ч" bodyClassName="px-2 pt-2">
          {s.events24h === 0 ? <Empty>Событий за сутки нет</Empty> : <EChart option={distributionOption} height={260} />}
        </Panel>
      </div>

      <div className="grid grid-cols-1 gap-3 xl:grid-cols-3">
        <Panel title="Последние угрозы" className="xl:col-span-2" actions={<Link to="/alerts" className="text-xs text-accent hover:underline">Все предупреждения →</Link>} bodyClassName="">
          {s.recentAlerts.length === 0 ? (
            <Empty>Угроз не обнаружено</Empty>
          ) : (
            <table className="table">
              <thead>
                <tr>
                  <th>Время</th>
                  <th>Критичность</th>
                  <th>Предупреждение</th>
                  <th className="text-right">Событий</th>
                  <th>Статус</th>
                </tr>
              </thead>
              <tbody>
                {s.recentAlerts.map((alert) => (
                  <tr key={alert.id}>
                    <td className="tabular-nums text-muted">{formatTime(alert.detectedAt)}</td>
                    <td><SeverityBadge severity={alert.severity} /></td>
                    <td className="max-w-[420px] truncate">
                      <Link to={`/alerts?open=${alert.id}`} className="hover:text-accent">{alert.title}</Link>
                    </td>
                    <td className="text-right tabular-nums">{formatNumber(alert.eventCount)}</td>
                    <td><StatusBadge status={alert.status} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </Panel>
        <div className="flex flex-col gap-3">
          <Panel title="Активные IP-адреса (24 ч)" bodyClassName="px-2 pt-1">
            {topIps.data && topIps.data.length > 0 ? <EChart option={topIpOption} height={Math.max(120, topIps.data.length * 26 + 30)} /> : <Empty>Нет событий с IP</Empty>}
          </Panel>
          <Panel title="Источники событий" bodyClassName="">
            <table className="table">
              <tbody>
                {s.sources.map((source) => (
                  <tr key={source.id}>
                    <td>
                      <div className="text-ink">{source.name}</div>
                      <div className="text-[11px] text-faint">{source.kind}</div>
                    </td>
                    <td className="text-right text-xs text-muted">{formatRelative(source.lastSeenAt)}</td>
                    <td className="w-[110px] text-right"><StatusBadge status={source.status} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </Panel>
        </div>
      </div>
    </>
  )
}

function buildTimeline(points: TimelinePoint[], range: RangeKey) {
  const { hours, step } = RANGES[range]
  const end = Math.floor(Date.now() / step) * step
  const start = end - hours * 3600_000 + step
  const buckets: number[] = []
  for (let t = start; t <= end; t += step) buckets.push(t)
  const index = new Map(buckets.map((t, i) => [t, i]))
  const series = new Map<Severity, number[]>(SEVERITIES.map((s) => [s, buckets.map(() => 0)]))
  for (const point of points) {
    const i = index.get(new Date(point.bucket).getTime())
    if (i !== undefined) series.get(point.severity)![i] += point.count
  }
  const label = (t: number) => new Date(t).toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' })
  return {
    ...chartBase,
    grid: { left: 44, right: 12, top: 28, bottom: 24 },
    legend: { top: 0, right: 8, itemWidth: 10, itemHeight: 10, textStyle: { color: '#8b93a1', fontSize: 11 } },
    tooltip: { ...chartBase.tooltip, trigger: 'axis', axisPointer: { type: 'shadow' } },
    xAxis: { type: 'category', data: buckets.map(label), axisLine: chartBase.axisLine, axisTick: { show: false }, axisLabel: { color: '#8b93a1' } },
    yAxis: { type: 'value', minInterval: 1, splitLine: chartBase.splitLine, axisLabel: { color: '#8b93a1' } },
    series: [...SEVERITIES].reverse().map((severity) => ({
      name: severity,
      type: 'bar',
      stack: 'events',
      barMaxWidth: 18,
      itemStyle: { color: SEVERITY_COLOR[severity] },
      emphasis: { focus: 'series' },
      data: series.get(severity)
    }))
  }
}

function buildDistribution(summary: DashboardSummary | undefined) {
  const data = (summary?.severityDistribution24h ?? [])
    .filter((d) => d.count > 0)
    .map((d) => ({ name: d.severity, value: d.count, itemStyle: { color: SEVERITY_COLOR[d.severity] } }))
  return {
    ...chartBase,
    tooltip: { ...chartBase.tooltip, trigger: 'item', formatter: '{b}: {c} ({d}%)' },
    legend: { bottom: 0, itemWidth: 10, itemHeight: 10, textStyle: { color: '#8b93a1', fontSize: 11 } },
    series: [
      {
        type: 'pie',
        radius: ['52%', '76%'],
        center: ['50%', '44%'],
        avoidLabelOverlap: true,
        itemStyle: { borderColor: '#171a20', borderWidth: 2 },
        label: { show: true, color: '#d9dde4', formatter: '{c}', fontSize: 11 },
        labelLine: { length: 6, length2: 6 },
        data
      }
    ]
  }
}

function buildTopIps(items: TopIp[]) {
  const sorted = [...items].reverse()
  return {
    ...chartBase,
    grid: { left: 110, right: 40, top: 6, bottom: 6 },
    tooltip: { ...chartBase.tooltip, trigger: 'axis', axisPointer: { type: 'shadow' } },
    xAxis: { type: 'value', show: false },
    yAxis: {
      type: 'category',
      data: sorted.map((i) => i.ip),
      axisLine: { show: false },
      axisTick: { show: false },
      axisLabel: { color: '#d9dde4', fontFamily: 'JetBrains Mono, Consolas, monospace', fontSize: 11 }
    },
    series: [
      {
        type: 'bar',
        data: sorted.map((i) => i.count),
        barMaxWidth: 14,
        itemStyle: { color: '#38bdf8', borderRadius: [0, 3, 3, 0] },
        label: { show: true, position: 'right', color: '#8b93a1', fontSize: 11 }
      }
    ]
  }
}
