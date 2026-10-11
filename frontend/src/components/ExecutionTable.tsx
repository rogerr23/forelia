import type { Execution } from '../api/types'
import { Badge } from './Badge'
import { instant, rate, resultLink } from '../utils/format'

export function ExecutionTable({ items }: { items: Execution[] }) {
  return (
    <div className="fl-scroll">
      <table>
        <thead>
          <tr>
            <th>Execução</th>
            <th>Criação</th>
            <th>Situação</th>
            <th>Taxa</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {items.map((item) => (
            <tr key={item.id}>
              <td>
                <span className="fl-id">{item.id}</span>
                <br />
                <span className="fl-small">
                  {item.totalItens} itens · {item.versaoRegras}
                </span>
              </td>
              <td>{instant(item.criadaEm)}</td>
              <td>
                <Badge value={item.status} />
              </td>
              <td>
                {item.status === 'CONCLUIDA' ? rate(item.taxaConciliacao) : '—'}
              </td>
              <td>
                <a className="fl-link" href={resultLink(item.id)}>
                  Abrir
                </a>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {items.length === 0 && (
        <p className="fl-empty">Nenhuma execução registrada.</p>
      )}
    </div>
  )
}
