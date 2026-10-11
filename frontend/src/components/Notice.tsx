import { Info, CircleAlert, CircleCheck } from 'lucide-react'
import type { ReactNode } from 'react'

export function Notice({
  children,
  kind = 'info',
}: {
  children: ReactNode
  kind?: 'info' | 'error' | 'success'
}) {
  const Icon =
    kind === 'error' ? CircleAlert : kind === 'success' ? CircleCheck : Info
  return (
    <div
      className={`fl-notice fl-alert-${kind}`}
      role={kind === 'error' ? 'alert' : 'status'}
    >
      <Icon aria-hidden />
      <div>{children}</div>
    </div>
  )
}
