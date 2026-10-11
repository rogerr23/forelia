import { CircleCheck, CircleDot, Clock3, TriangleAlert } from 'lucide-react'
import { labels } from '../utils/format'

export function Badge({ value }: { value: string }) {
  const kind = ['CONCLUIDA', 'CONCILIADO', 'ANALISADA', 'DISPONIVEL'].includes(
    value,
  )
    ? 'success'
    : ['AGUARDANDO', 'PENDENTE'].includes(value)
      ? 'pending'
      : [
            'DIVERGENTE',
            'SEM_REGISTRO_CONTABIL',
            'SEM_OPERACAO_FINANCEIRA',
            'FALHOU',
          ].includes(value)
        ? 'warning'
        : 'info'
  const Icon =
    kind === 'success'
      ? CircleCheck
      : kind === 'pending'
        ? Clock3
        : kind === 'warning'
          ? TriangleAlert
          : CircleDot
  return (
    <span className={`fl-badge fl-status-${kind}`}>
      <Icon aria-hidden />
      {labels[value] || value}
    </span>
  )
}
