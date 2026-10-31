import { createContext, useCallback, useContext, useMemo, useRef, useState, type ReactNode } from 'react'
import { useNavigate } from 'react-router'
import type { Severity } from '../api/types'
import { SEVERITY_COLOR } from '../lib/format'

interface Toast {
  id: number
  tone: Severity | 'error' | 'success'
  title: string
  body?: string
  link?: string
}

interface ToastApi {
  push: (toast: Omit<Toast, 'id'>) => void
  error: (message: string) => void
  success: (message: string) => void
}

const ToastContext = createContext<ToastApi | null>(null)

const toneColor = (tone: Toast['tone']) =>
  tone === 'error' ? '#ef4444' : tone === 'success' ? '#22c55e' : SEVERITY_COLOR[tone]

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([])
  const counter = useRef(0)
  const navigate = useNavigate()

  const dismiss = useCallback((id: number) => setToasts((all) => all.filter((t) => t.id !== id)), [])

  const push = useCallback(
    (toast: Omit<Toast, 'id'>) => {
      const id = ++counter.current
      setToasts((all) => [...all.slice(-4), { ...toast, id }])
      setTimeout(() => dismiss(id), toast.tone === 'error' ? 8000 : 6000)
    },
    [dismiss]
  )

  const value = useMemo<ToastApi>(
    () => ({
      push,
      error: (message) => push({ tone: 'error', title: message }),
      success: (message) => push({ tone: 'success', title: message })
    }),
    [push]
  )

  return (
    <ToastContext.Provider value={value}>
      {children}
      <div className="pointer-events-none fixed right-4 bottom-4 z-50 flex w-[360px] flex-col gap-2">
        {toasts.map((toast) => (
          <div
            key={toast.id}
            className="pointer-events-auto cursor-pointer rounded-md border border-line bg-panel-2 px-3 py-2.5 shadow-xl shadow-black/40"
            style={{ borderLeft: `3px solid ${toneColor(toast.tone)}` }}
            onClick={() => {
              if (toast.link) navigate(toast.link)
              dismiss(toast.id)
            }}
          >
            <div className="text-[13px] font-medium text-ink-strong">{toast.title}</div>
            {toast.body && <div className="mt-0.5 truncate text-xs text-muted">{toast.body}</div>}
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  )
}

export function useToasts(): ToastApi {
  const context = useContext(ToastContext)
  if (!context) throw new Error('ToastProvider is missing')
  return context
}
