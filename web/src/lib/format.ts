import type { Severity } from '../api/types'

const dateTime = new Intl.DateTimeFormat('ru-RU', {
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit'
})

const time = new Intl.DateTimeFormat('ru-RU', { hour: '2-digit', minute: '2-digit', second: '2-digit' })
const number = new Intl.NumberFormat('ru-RU')

export const formatDateTime = (value: string | null | undefined) => (value ? dateTime.format(new Date(value)) : '—')
export const formatTime = (value: string | null | undefined) => (value ? time.format(new Date(value)) : '—')
export const formatNumber = (value: number | null | undefined) => (value === null || value === undefined ? '—' : number.format(value))

export function formatRelative(value: string | null | undefined): string {
  if (!value) return 'никогда'
  const seconds = Math.round((Date.now() - new Date(value).getTime()) / 1000)
  if (seconds < 5) return 'только что'
  if (seconds < 60) return `${seconds} с назад`
  const minutes = Math.round(seconds / 60)
  if (minutes < 60) return `${minutes} мин назад`
  const hours = Math.round(minutes / 60)
  if (hours < 48) return `${hours} ч назад`
  return `${Math.round(hours / 24)} дн назад`
}

export const SEVERITY_COLOR: Record<Severity, string> = {
  Critical: '#ef4444',
  High: '#f97316',
  Medium: '#eab308',
  Low: '#7c9cc4',
  Info: '#6b7280'
}

export const SEVERITY_LABEL: Record<Severity, string> = {
  Critical: 'Critical',
  High: 'High',
  Medium: 'Medium',
  Low: 'Low',
  Info: 'Info'
}

export function toLocalInput(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`
}

export function fromLocalInput(value: string): string | undefined {
  if (!value) return undefined
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? undefined : date.toISOString()
}
