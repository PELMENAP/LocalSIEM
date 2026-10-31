import { useEffect, useState } from 'react'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import {
  GROUP_FIELDS,
  SEVERITIES,
  type AuditEntry,
  type PagedResult,
  type RoleName,
  type Rule,
  type RuleInput,
  type RuleType,
  type Severity,
  type Source,
  type SourceKind,
  type SourceWithKey,
  type SystemSettings,
  type User
} from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { useToasts } from '../components/Toasts'
import { ChipToggle, Empty, ErrorBox, Modal, PageHeader, Pagination, Panel, SeverityBadge, Spinner, StatusBadge, Toggle, errorText } from '../components/ui'
import { formatDateTime, formatRelative } from '../lib/format'
import { describeRule } from './DetectionPage'

const TABS = [
  { key: 'users', label: 'Пользователи и роли' },
  { key: 'sources', label: 'Источники событий' },
  { key: 'rules', label: 'Правила обнаружения' },
  { key: 'system', label: 'Система' },
  { key: 'audit', label: 'Журнал действий' }
] as const

type TabKey = (typeof TABS)[number]['key']

export function SettingsPage() {
  const { isAdmin } = useAuth()
  const [tab, setTab] = useState<TabKey>('users')
  if (!isAdmin) {
    return (
      <>
        <PageHeader title="Settings" />
        <div className="panel p-6 text-sm text-muted">Раздел доступен только администратору.</div>
      </>
    )
  }
  return (
    <>
      <PageHeader title="Settings" subtitle="Управление пользователями, источниками, правилами и параметрами системы" />
      <div className="mb-3 flex gap-1 border-b border-line">
        {TABS.map((t) => (
          <button
            key={t.key}
            className={`-mb-px border-b-2 px-3 py-2 text-[13px] transition-colors ${tab === t.key ? 'border-accent text-ink-strong' : 'border-transparent text-muted hover:text-ink'}`}
            onClick={() => setTab(t.key)}
          >
            {t.label}
          </button>
        ))}
      </div>
      {tab === 'users' && <UsersTab />}
      {tab === 'sources' && <SourcesTab />}
      {tab === 'rules' && <RulesTab />}
      {tab === 'system' && <SystemTab />}
      {tab === 'audit' && <AuditTab />}
    </>
  )
}

function useInvalidate(key: string) {
  const queryClient = useQueryClient()
  return () => queryClient.invalidateQueries({ queryKey: [key] })
}

function UsersTab() {
  const { user: me } = useAuth()
  const toasts = useToasts()
  const invalidate = useInvalidate('users')
  const [editing, setEditing] = useState<User | 'new' | null>(null)
  const [resetFor, setResetFor] = useState<User | null>(null)
  const users = useQuery({ queryKey: ['users'], queryFn: () => api.get<User[]>('/api/users') })
  const setActive = useMutation({
    mutationFn: ({ id, isActive }: { id: string; isActive: boolean }) => api.patch(`/api/users/${id}/active`, { isActive }),
    onSuccess: invalidate,
    onError: (e) => toasts.error(errorText(e))
  })

  return (
    <Panel title="Пользователи" actions={<button className="btn btn-primary h-7" onClick={() => setEditing('new')}>Добавить</button>} bodyClassName="">
      {users.isLoading && <Spinner />}
      {users.error && <ErrorBox error={users.error} />}
      {users.data && (
        <table className="table">
          <thead>
            <tr>
              <th>Логин</th>
              <th>Имя</th>
              <th>E-mail</th>
              <th>Роль</th>
              <th>Последний вход</th>
              <th>Активен</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {users.data.map((u) => (
              <tr key={u.id}>
                <td className="mono">{u.username}</td>
                <td>{u.displayName}</td>
                <td className="text-muted">{u.email ?? '—'}</td>
                <td>{u.role === 'Administrator' ? 'Administrator' : 'Security Analyst'}</td>
                <td className="text-muted">{formatRelative(u.lastLoginAt)}</td>
                <td><Toggle checked={u.isActive} disabled={u.id === me?.id} onChange={(isActive) => setActive.mutate({ id: u.id, isActive })} /></td>
                <td className="text-right whitespace-nowrap">
                  <button className="btn btn-ghost h-7 text-xs" onClick={() => setEditing(u)}>Изменить</button>
                  <button className="btn btn-ghost h-7 text-xs" onClick={() => setResetFor(u)}>Сменить пароль</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <div className="border-t border-line px-4 py-3 text-xs text-muted">
        <b className="text-ink">Administrator</b> — полный доступ: пользователи, правила, источники, настройки, очистка демо-данных.{' '}
        <b className="text-ink">Security Analyst</b> — события, предупреждения, расследование инцидентов, комментарии, запуск симулятора.
      </div>
      {editing && <UserModal user={editing === 'new' ? null : editing} onClose={() => setEditing(null)} onSaved={invalidate} />}
      {resetFor && <ResetPasswordModal user={resetFor} onClose={() => setResetFor(null)} />}
    </Panel>
  )
}

function UserModal({ user, onClose, onSaved }: { user: User | null; onClose: () => void; onSaved: () => void }) {
  const [username, setUsername] = useState(user?.username ?? '')
  const [displayName, setDisplayName] = useState(user?.displayName ?? '')
  const [email, setEmail] = useState(user?.email ?? '')
  const [role, setRole] = useState<RoleName>(user?.role ?? 'SecurityAnalyst')
  const [password, setPassword] = useState('')
  const save = useMutation({
    mutationFn: () =>
      user
        ? api.put(`/api/users/${user.id}`, { displayName, email: email || null, role })
        : api.post('/api/users', { username, displayName, email: email || null, role, password }),
    onSuccess: () => {
      onSaved()
      onClose()
    }
  })
  return (
    <Modal
      title={user ? `Пользователь ${user.username}` : 'Новый пользователь'}
      onClose={onClose}
      footer={
        <>
          <button className="btn" onClick={onClose}>Отмена</button>
          <button className="btn btn-primary" disabled={save.isPending} onClick={() => save.mutate()}>Сохранить</button>
        </>
      }
    >
      <div className="grid grid-cols-2 gap-3">
        <div>
          <label className="label">Логин</label>
          <input className="field w-full" disabled={Boolean(user)} value={username} onChange={(e) => setUsername(e.target.value)} />
        </div>
        <div>
          <label className="label">Роль</label>
          <select className="field w-full" value={role} onChange={(e) => setRole(e.target.value as RoleName)}>
            <option value="SecurityAnalyst">Security Analyst</option>
            <option value="Administrator">Administrator</option>
          </select>
        </div>
        <div>
          <label className="label">Отображаемое имя</label>
          <input className="field w-full" value={displayName} onChange={(e) => setDisplayName(e.target.value)} />
        </div>
        <div>
          <label className="label">E-mail</label>
          <input className="field w-full" value={email} onChange={(e) => setEmail(e.target.value)} />
        </div>
        {!user && (
          <div className="col-span-2">
            <label className="label">Пароль (не короче 8 символов)</label>
            <input type="password" className="field w-full" value={password} onChange={(e) => setPassword(e.target.value)} />
          </div>
        )}
      </div>
      {save.error && <ErrorBox error={save.error} />}
    </Modal>
  )
}

function ResetPasswordModal({ user, onClose }: { user: User; onClose: () => void }) {
  const toasts = useToasts()
  const [password, setPassword] = useState('')
  const reset = useMutation({
    mutationFn: () => api.post(`/api/users/${user.id}/reset-password`, { password }),
    onSuccess: () => {
      toasts.success('Пароль изменён')
      onClose()
    }
  })
  return (
    <Modal
      title={`Новый пароль для ${user.username}`}
      onClose={onClose}
      footer={
        <>
          <button className="btn" onClick={onClose}>Отмена</button>
          <button className="btn btn-primary" disabled={password.length < 8 || reset.isPending} onClick={() => reset.mutate()}>Сохранить</button>
        </>
      }
    >
      <input type="password" className="field w-full" autoFocus value={password} onChange={(e) => setPassword(e.target.value)} />
      {reset.error && <ErrorBox error={reset.error} />}
    </Modal>
  )
}

function SourcesTab() {
  const toasts = useToasts()
  const invalidate = useInvalidate('sources')
  const [creating, setCreating] = useState(false)
  const [issuedKey, setIssuedKey] = useState<SourceWithKey | null>(null)
  const sources = useQuery({ queryKey: ['sources'], queryFn: () => api.get<Source[]>('/api/sources') })
  const update = useMutation({
    mutationFn: (s: Source) => api.put(`/api/sources/${s.id}`, { name: s.name, isEnabled: !s.isEnabled }),
    onSuccess: invalidate,
    onError: (e) => toasts.error(errorText(e))
  })
  const rotate = useMutation({
    mutationFn: (id: string) => api.post<SourceWithKey>(`/api/sources/${id}/rotate-key`),
    onSuccess: (result) => {
      setIssuedKey(result)
      invalidate()
    },
    onError: (e) => toasts.error(errorText(e))
  })

  return (
    <Panel title="Источники событий" actions={<button className="btn btn-primary h-7" onClick={() => setCreating(true)}>Добавить источник</button>} bodyClassName="">
      {sources.isLoading && <Spinner />}
      {sources.error && <ErrorBox error={sources.error} />}
      {sources.data && (
        <table className="table">
          <thead>
            <tr>
              <th>Название</th>
              <th>Тип</th>
              <th>Статус</th>
              <th>Последнее событие</th>
              <th>Ключ API</th>
              <th>Включён</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {sources.data.map((s) => (
              <tr key={s.id}>
                <td className="text-ink-strong">{s.name}</td>
                <td className="text-muted">{s.kind}</td>
                <td><StatusBadge status={s.status} /></td>
                <td className="text-muted">{formatRelative(s.lastSeenAt)}</td>
                <td className="text-muted">{s.kind === 'Simulator' ? 'не нужен' : s.hasApiKey ? 'выдан' : '—'}</td>
                <td><Toggle checked={s.isEnabled} onChange={() => update.mutate(s)} /></td>
                <td className="text-right">
                  {s.kind !== 'Simulator' && (
                    <button className="btn btn-ghost h-7 text-xs" onClick={() => rotate.mutate(s.id)}>Перевыпустить ключ</button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <div className="border-t border-line px-4 py-3 text-xs text-muted">
        Внешние источники отправляют события в <span className="mono text-ink">POST /api/ingest/events</span> с заголовком{' '}
        <span className="mono text-ink">X-Api-Key</span>. Ключ показывается один раз — сохраните его при выдаче.
      </div>
      {creating && (
        <CreateSourceModal
          onClose={() => setCreating(false)}
          onCreated={(result) => {
            setCreating(false)
            setIssuedKey(result)
            invalidate()
          }}
        />
      )}
      {issuedKey && <IssuedKeyModal result={issuedKey} onClose={() => setIssuedKey(null)} />}
    </Panel>
  )
}

function CreateSourceModal({ onClose, onCreated }: { onClose: () => void; onCreated: (result: SourceWithKey) => void }) {
  const [name, setName] = useState('')
  const [kind, setKind] = useState<SourceKind>('HttpApi')
  const create = useMutation({ mutationFn: () => api.post<SourceWithKey>('/api/sources', { name, kind }), onSuccess: onCreated })
  return (
    <Modal
      title="Новый источник"
      onClose={onClose}
      footer={
        <>
          <button className="btn" onClick={onClose}>Отмена</button>
          <button className="btn btn-primary" disabled={!name.trim() || create.isPending} onClick={() => create.mutate()}>Создать</button>
        </>
      }
    >
      <div className="grid grid-cols-2 gap-3">
        <div>
          <label className="label">Название</label>
          <input className="field w-full" value={name} onChange={(e) => setName(e.target.value)} autoFocus />
        </div>
        <div>
          <label className="label">Тип</label>
          <select className="field w-full" value={kind} onChange={(e) => setKind(e.target.value as SourceKind)}>
            <option value="HttpApi">HTTP API</option>
            <option value="WindowsCollector">Windows Collector</option>
          </select>
        </div>
      </div>
      {create.error && <ErrorBox error={create.error} />}
    </Modal>
  )
}

function IssuedKeyModal({ result, onClose }: { result: SourceWithKey; onClose: () => void }) {
  const toasts = useToasts()
  const curl = `curl -X POST ${window.location.origin}/api/ingest/events -H "Content-Type: application/json" -H "X-Api-Key: ${result.apiKey}" -d '{"events":[{"timestamp":"${new Date().toISOString()}","eventType":"LoginFailed","sourceIp":"192.0.2.10","username":"alice","message":"test"}]}'`
  return (
    <Modal title={`Ключ API: ${result.source.name}`} onClose={onClose} width={640} footer={<button className="btn btn-primary" onClick={onClose}>Готово</button>}>
      <p className="mb-2 text-xs text-muted">Ключ больше не будет показан. Скопируйте его сейчас.</p>
      <div className="mb-4 flex gap-2">
        <input className="field mono flex-1" readOnly value={result.apiKey} onFocus={(e) => e.target.select()} />
        <button
          className="btn"
          onClick={() => {
            void navigator.clipboard?.writeText(result.apiKey)
            toasts.success('Ключ скопирован')
          }}
        >
          Копировать
        </button>
      </div>
      <div className="label">Проверка через curl</div>
      <pre className="mono overflow-auto rounded-md border border-line bg-bg p-3 text-[11px] whitespace-pre-wrap break-all">{curl}</pre>
    </Modal>
  )
}

const RULE_TYPES: RuleType[] = ['Threshold', 'DistinctCount', 'Sequence', 'SingleEvent']

function RulesTab() {
  const invalidate = useInvalidate('rules')
  const [editing, setEditing] = useState<Rule | 'new' | null>(null)
  const rules = useQuery({ queryKey: ['rules'], queryFn: () => api.get<Rule[]>('/api/rules') })
  return (
    <Panel title="Правила обнаружения" actions={<button className="btn btn-primary h-7" onClick={() => setEditing('new')}>Новое правило</button>} bodyClassName="">
      {rules.isLoading && <Spinner />}
      {rules.error && <ErrorBox error={rules.error} />}
      {rules.data && (
        <table className="table">
          <thead>
            <tr>
              <th>Правило</th>
              <th>Логика</th>
              <th>Критичность</th>
              <th>Cooldown</th>
              <th>Состояние</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {rules.data.map((r) => (
              <tr key={r.id}>
                <td>
                  <div className="text-ink-strong">{r.name}</div>
                  <div className="mono text-[11px] text-faint">{r.code}</div>
                </td>
                <td className="max-w-[520px] text-muted">{describeRule(r)}</td>
                <td><SeverityBadge severity={r.severity} /></td>
                <td className="text-muted">{r.cooldownSeconds ?? r.windowSeconds} с</td>
                <td className={r.isEnabled ? 'text-emerald-300' : 'text-faint'}>{r.isEnabled ? 'включено' : 'отключено'}</td>
                <td className="text-right"><button className="btn btn-ghost h-7 text-xs" onClick={() => setEditing(r)}>Изменить</button></td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {editing && <RuleModal rule={editing === 'new' ? null : editing} onClose={() => setEditing(null)} onSaved={invalidate} />}
    </Panel>
  )
}

function RuleModal({ rule, onClose, onSaved }: { rule: Rule | null; onClose: () => void; onSaved: () => void }) {
  const [form, setForm] = useState<RuleInput>(() =>
    rule
      ? {
          name: rule.name,
          description: rule.description,
          ruleType: rule.ruleType,
          eventType: rule.eventType,
          threshold: rule.threshold,
          windowSeconds: rule.windowSeconds,
          cooldownSeconds: rule.cooldownSeconds,
          groupBy: rule.groupBy,
          parameters: rule.parameters,
          severity: rule.severity,
          isEnabled: rule.isEnabled,
          version: rule.version
        }
      : {
          code: '',
          name: '',
          description: '',
          ruleType: 'Threshold',
          eventType: 'LoginFailed',
          threshold: 5,
          windowSeconds: 60,
          cooldownSeconds: null,
          groupBy: ['SourceIp'],
          parameters: {},
          severity: 'Medium',
          isEnabled: true
        }
  )
  const [values, setValues] = useState((rule?.parameters.values ?? []).join(', '))
  const set = <K extends keyof RuleInput>(key: K, value: RuleInput[K]) => setForm((f) => ({ ...f, [key]: value }))
  const setParam = (key: keyof RuleInput['parameters'], value: string) =>
    setForm((f) => ({ ...f, parameters: { ...f.parameters, [key]: value || null } }))

  const save = useMutation({
    mutationFn: () => {
      const parsedValues = values.split(',').map((v) => v.trim()).filter(Boolean)
      const body: RuleInput = { ...form, parameters: { ...form.parameters, values: parsedValues.length ? parsedValues : null } }
      return rule ? api.put<Rule>(`/api/rules/${rule.id}`, body) : api.post<Rule>('/api/rules', body)
    },
    onSuccess: () => {
      onSaved()
      onClose()
    }
  })

  const num = (value: string) => (value === '' ? 0 : Number(value))

  return (
    <Modal
      title={rule ? `Правило ${rule.code}` : 'Новое правило'}
      onClose={onClose}
      width={720}
      footer={
        <>
          <button className="btn" onClick={onClose}>Отмена</button>
          <button className="btn btn-primary" disabled={save.isPending} onClick={() => save.mutate()}>Сохранить</button>
        </>
      }
    >
      <div className="grid grid-cols-2 gap-3">
        {!rule && (
          <div>
            <label className="label">Код (A-Z, 0-9, _)</label>
            <input className="field mono w-full" value={form.code ?? ''} onChange={(e) => set('code', e.target.value.toUpperCase())} />
          </div>
        )}
        <div className={rule ? 'col-span-2' : ''}>
          <label className="label">Название</label>
          <input className="field w-full" value={form.name} onChange={(e) => set('name', e.target.value)} />
        </div>
        <div className="col-span-2">
          <label className="label">Описание</label>
          <textarea className="field w-full" rows={2} value={form.description} onChange={(e) => set('description', e.target.value)} />
        </div>
        <div>
          <label className="label">Тип правила</label>
          <select className="field w-full" value={form.ruleType} onChange={(e) => set('ruleType', e.target.value as RuleType)}>
            {RULE_TYPES.map((t) => <option key={t} value={t}>{t}</option>)}
          </select>
        </div>
        <div>
          <label className="label">Тип события</label>
          <input className="field w-full" value={form.eventType} onChange={(e) => set('eventType', e.target.value)} />
        </div>
        <div>
          <label className="label">Порог</label>
          <input type="number" min={1} className="field w-full" disabled={form.ruleType === 'SingleEvent'} value={form.threshold} onChange={(e) => set('threshold', num(e.target.value))} />
        </div>
        <div>
          <label className="label">Окно, с</label>
          <input type="number" min={1} className="field w-full" value={form.windowSeconds} onChange={(e) => set('windowSeconds', num(e.target.value))} />
        </div>
        <div>
          <label className="label">Cooldown, с (пусто = окно)</label>
          <input
            type="number"
            min={0}
            className="field w-full"
            value={form.cooldownSeconds ?? ''}
            onChange={(e) => set('cooldownSeconds', e.target.value === '' ? null : Number(e.target.value))}
          />
        </div>
        <div>
          <label className="label">Критичность</label>
          <select className="field w-full" value={form.severity} onChange={(e) => set('severity', e.target.value as Severity)}>
            {SEVERITIES.map((s) => <option key={s} value={s}>{s}</option>)}
          </select>
        </div>
        <div className="col-span-2">
          <div className="label">Группировка</div>
          <ChipToggle options={GROUP_FIELDS} value={form.groupBy as (typeof GROUP_FIELDS)[number][]} onChange={(v) => set('groupBy', v)} />
        </div>
        {form.ruleType === 'DistinctCount' && (
          <div>
            <label className="label">Поле для подсчёта различных значений</label>
            <select className="field w-full" value={form.parameters.distinctField ?? ''} onChange={(e) => setParam('distinctField', e.target.value)}>
              <option value="">—</option>
              {GROUP_FIELDS.map((f) => <option key={f} value={f}>{f}</option>)}
            </select>
          </div>
        )}
        {form.ruleType === 'Sequence' && (
          <div>
            <label className="label">Затем событие</label>
            <input className="field w-full" value={form.parameters.thenEventType ?? ''} onChange={(e) => setParam('thenEventType', e.target.value)} />
          </div>
        )}
        <div>
          <label className="label">{form.ruleType === 'SingleEvent' ? 'Поле условия' : 'Фильтр: поле (необязательно)'}</label>
          <input className="field w-full" placeholder="newRole / protected" value={form.parameters.field ?? ''} onChange={(e) => setParam('field', e.target.value)} />
        </div>
        <div>
          <label className="label">Значения через запятую</label>
          <input className="field w-full" placeholder="Administrator, Domain Admins" value={values} onChange={(e) => setValues(e.target.value)} />
        </div>
        <div className="col-span-2 flex items-center gap-2">
          <Toggle checked={form.isEnabled} onChange={(v) => set('isEnabled', v)} />
          <span className="text-[13px]">Правило активно</span>
        </div>
      </div>
      {save.error && <ErrorBox error={save.error} />}
    </Modal>
  )
}

function SystemTab() {
  const toasts = useToasts()
  const settings = useQuery({ queryKey: ['settings'], queryFn: () => api.get<SystemSettings>('/api/settings') })
  const [form, setForm] = useState<SystemSettings | null>(null)
  useEffect(() => {
    if (settings.data) setForm(settings.data)
  }, [settings.data])
  const save = useMutation({
    mutationFn: () => api.put<SystemSettings>('/api/settings', form),
    onSuccess: () => toasts.success('Настройки сохранены'),
    onError: (e) => toasts.error(errorText(e))
  })
  if (settings.isLoading || !form) return <Spinner />
  const field = (key: keyof SystemSettings, label: string, hint: string) => (
    <div>
      <label className="label">{label}</label>
      <input type="number" className="field w-full" value={form[key]} onChange={(e) => setForm({ ...form, [key]: Number(e.target.value) })} />
      <div className="mt-1 text-[11px] text-faint">{hint}</div>
    </div>
  )
  return (
    <Panel title="Параметры системы" className="max-w-[720px]">
      <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
        {field('maxLinkedEventsPerAlert', 'Событий на алерт', 'Сколько исходных событий хранится в связке с одним предупреждением')}
        {field('sourceIdleSeconds', 'Простой источника, с', 'После этого времени без событий источник считается Idle')}
        {field('simulatorMaxRatePerSecond', 'Скорость симулятора', 'Максимум событий в секунду для сценариев')}
      </div>
      <div className="mt-4 flex justify-end">
        <button className="btn btn-primary" disabled={save.isPending} onClick={() => save.mutate()}>Сохранить</button>
      </div>
    </Panel>
  )
}

function AuditTab() {
  const [page, setPage] = useState(1)
  const [action, setAction] = useState('')
  const audit = useQuery({
    queryKey: ['audit', page, action],
    queryFn: () => api.get<PagedResult<AuditEntry>>('/api/audit', { page, pageSize: 50, action }),
    placeholderData: keepPreviousData
  })
  return (
    <Panel
      title="Журнал действий пользователей"
      actions={<input className="field h-7 w-56" placeholder="фильтр: incident., auth., rule." value={action} onChange={(e) => { setAction(e.target.value); setPage(1) }} />}
      bodyClassName=""
    >
      {audit.error && <ErrorBox error={audit.error} />}
      {audit.data && audit.data.items.length === 0 && <Empty>Записей нет</Empty>}
      {audit.data && audit.data.items.length > 0 && (
        <table className="table">
          <thead>
            <tr>
              <th>Время</th>
              <th>Пользователь</th>
              <th>Действие</th>
              <th>Объект</th>
              <th>Детали</th>
              <th>IP</th>
            </tr>
          </thead>
          <tbody>
            {audit.data.items.map((a) => (
              <tr key={a.id}>
                <td className="tabular-nums whitespace-nowrap text-muted">{formatDateTime(a.createdAt)}</td>
                <td>{a.username ?? <span className="text-faint">—</span>}</td>
                <td className="mono">{a.action}</td>
                <td className="text-muted">{a.entityType ?? ''}</td>
                <td className="mono max-w-[420px] truncate text-[11px] text-muted">{a.details ?? ''}</td>
                <td className="mono text-muted">{a.ipAddress ?? ''}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {audit.data && <Pagination page={page} pageSize={50} total={audit.data.totalCount} onPage={setPage} />}
    </Panel>
  )
}
