import { useState } from 'react'
import { useRemote } from '../hooks/useRemote'
import { ExecutionTable } from '../components/ExecutionTable'
import { Plus, RefreshCw } from 'lucide-react'
import type { Dashboard } from '../api/types'
import { instant, labels, rate, resultLink } from '../utils/format'
import { Heading } from '../components/Heading'
import { Loading } from '../components/Loading'
import { Notice } from '../components/Notice'

export function DashboardPage() {
  const [selection, setSelection] = useState('')
  const [revision, setRevision] = useState(0)
  const remote = useRemote<Dashboard>(
    '/dashboard' + (selection ? '?conciliacaoId=' + selection : ''),
    revision,
  )
  const data = remote.data
  const options = data
    ? [...data.execucoesRecentes.filter((item) => item.status === 'CONCLUIDA')]
    : []
  if (
    data?.conciliacaoSelecionada &&
    !options.some((item) => item.id === data.conciliacaoSelecionada!.id)
  )
    options.unshift(data.conciliacaoSelecionada)
  return (
    <>
      <Heading
        title="Visão geral"
        sub="Acompanhe os resultados de cada conciliação."
      >
        <a className="fl-button fl-primary" href="#nova-conciliacao">
          <Plus aria-hidden />
          Nova conciliação
        </a>
      </Heading>
      <div className="fl-actions fl-refresh">
        <button
          className="fl-button"
          onClick={() => setRevision((value) => value + 1)}
          disabled={remote.loading}
        >
          <RefreshCw aria-hidden />
          Atualizar
        </button>
      </div>
      {remote.loading && <Loading />}
      {remote.error && <Notice kind="error">{remote.error}</Notice>}
      {data && (
        <>
          <section className="fl-panel">
            <div className="fl-panelhead">
              <div>
                <h2>Conciliação selecionada</h2>
                <span className="fl-small">
                  Os indicadores pertencem apenas à execução selecionada.
                </span>
              </div>
              <select
                className="fl-select"
                value={selection}
                onChange={(event) => setSelection(event.target.value)}
                aria-label="Selecionar conciliação"
              >
                <option value="">Concluída mais recente</option>
                {options.map((item) => (
                  <option key={item.id} value={item.id}>
                    {instant(item.criadaEm)} · {item.id.slice(0, 8)}
                  </option>
                ))}
              </select>
            </div>
            {!data.conciliacaoSelecionada && (
              <Notice>
                Não há conciliações concluídas. Importe os arquivos para
                começar.
              </Notice>
            )}
          </section>
          <div className="fl-stats">
            {[
              [
                'Taxa de conciliação',
                rate(data.indicadores.taxaConciliacao),
                `${data.indicadores.paresConciliados} de ${data.indicadores.totalItens} itens conciliados`,
              ],
              [
                'Pares conciliados',
                data.indicadores.paresConciliados,
                'Valores e datas iguais',
              ],
              [
                'Itens com divergência',
                data.indicadores.itensComDivergencia,
                'Inclui registros sem correspondência',
              ],
            ].map(([label, value, sub]) => (
              <div className="fl-stat" key={label}>
                <span className="fl-small">{label}</span>
                <strong>{value}</strong>
                <em>{sub}</em>
              </div>
            ))}
          </div>
          <section className="fl-panel">
            <div className="fl-panelhead">
              <div>
                <h2>Ocorrências por tipo</h2>
                <span className="fl-small">
                  {data.distribuicaoDivergencias.reduce(
                    (total, row) => total + row.ocorrencias,
                    0,
                  )}{' '}
                  ocorrências em {data.indicadores.itensComDivergencia} itens ·
                  um item pode ter mais de uma
                </span>
              </div>
              {data.conciliacaoSelecionada && (
                <a
                  className="fl-link"
                  href={resultLink(data.conciliacaoSelecionada.id)}
                >
                  Consultar resultados
                </a>
              )}
            </div>
            {data.distribuicaoDivergencias.map((row) => (
              <div className="fl-chartrow" key={row.tipo}>
                <span>{labels[row.tipo]}</span>
                <div className="fl-track">
                  <div
                    className="fl-fill"
                    style={{
                      width: `${(row.ocorrencias / Math.max(1, ...data.distribuicaoDivergencias.map((row) => row.ocorrencias))) * 100}%`,
                    }}
                  />
                </div>
                <span>{row.ocorrencias}</span>
              </div>
            ))}
            <span className="fl-small">
              {data.indicadores.totalRegistrosOrigem} registros de origem ·{' '}
              {data.indicadores.totalItens} itens de conciliação
            </span>
          </section>
          <section className="fl-panel">
            <div className="fl-panelhead">
              <h2>Execuções recentes</h2>
              <a className="fl-link" href="#historico">
                Ver histórico
              </a>
            </div>
            <ExecutionTable items={data.execucoesRecentes} />
          </section>
          <span className="fl-small">
            Histórico global: {data.execucoes.total} tentativas ·{' '}
            {data.execucoes.concluidas} concluídas · {data.execucoes.pendentes}{' '}
            pendentes · {data.execucoes.processando} processando ·{' '}
            {data.execucoes.falhas} falhas
          </span>
        </>
      )}
    </>
  )
}
