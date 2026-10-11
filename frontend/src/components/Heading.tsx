import type { ReactNode } from 'react'

export function Heading({
  title,
  sub,
  children,
}: {
  title: string
  sub: string
  children?: ReactNode
}) {
  return (
    <div className="fl-heading">
      <div>
        <h1>{title}</h1>
        <p>{sub}</p>
      </div>
      {children}
    </div>
  )
}
