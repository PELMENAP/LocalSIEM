import { useState, type FormEvent } from 'react'
import { useAuth } from '../auth/AuthContext'
import { errorText } from '../components/ui'

export function LoginPage() {
  const { login } = useAuth()
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await login(username, password)
    } catch (err) {
      setError(errorText(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="flex h-full items-center justify-center bg-bg p-4">
      <form onSubmit={submit} className="panel w-full max-w-[360px] p-6">
        <div className="mb-6 flex items-center gap-3">
          <img src="/favicon.svg" alt="" className="h-10 w-10" />
          <div>
            <div className="text-base font-semibold text-ink-strong">LocalSIEM</div>
            <div className="text-xs text-muted">Мониторинг событий информационной безопасности</div>
          </div>
        </div>
        <label className="label" htmlFor="username">Логин</label>
        <input id="username" className="field mb-3 w-full" autoFocus autoComplete="username" value={username} onChange={(e) => setUsername(e.target.value)} />
        <label className="label" htmlFor="password">Пароль</label>
        <input id="password" type="password" className="field mb-4 w-full" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} />
        {error && <div className="mb-3 rounded border border-sev-critical/40 bg-sev-critical/10 px-3 py-2 text-xs text-red-300">{error}</div>}
        <button className="btn btn-primary w-full" disabled={busy || !username || !password}>
          {busy ? 'Вход…' : 'Войти'}
        </button>
      </form>
    </div>
  )
}
