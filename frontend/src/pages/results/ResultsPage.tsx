import { useState } from 'react'
import type { FormEvent } from 'react'
import { Download, RefreshCw } from 'lucide-react'
import { exportCsv } from '../../api/client'
import type {
  Execution,
  Filters,
  ImportedFile,
  Item,
  Page,
} from '../../api/types'
import { emptyFilters, filterQuery } from '../../utils/filters'
import { amount, date, instant, labels, rate } from '../../utils/format'
import { useRemote } from '../../hooks/useRemote'
import { ItemDetails } from './ItemDetails'
import { Badge } from '../../components/Badge'
import { Heading } from '../../components/Heading'
import { Loading } from '../../components/Loading'
import { Notice } from '../../components/Notice'
import { Pagination } from '../../components/Pagination'

const statuses = ['PENDENTE', 'EM_ANALISE', 'ANALISADA']
const results = [
  'CONCILIADO',
  'DIVERGENTE',
  'SEM_REGISTRO_CONTABIL',
  'SEM_OPERACAO_FINANCEIRA',
]
const message = (error: unknown) =>
  error instanceof Error
    ? error.message
    : 'Não foi possível concluir a solicitação.'

export function ResultsPage({ id }: { id: string }) {
  const [revision, setRevision] = useState(0)
  const [draft, setDraft] = useState<Filters>({ ...emptyFilters })
  const [filters, setFilters] = useState<Filters>({ ...emptyFilters })
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState<string | null>(null)
  const [error, setError] = useState('')
  const [exporting, setExporting] = useState(false)
  const execution = useRemote<Execution>('/conciliacoes/' + id, revision)
  const summary = execution.data
  const operational = useRemote<ImportedFile>(
    summary ? '/importacoes/' + summary.arquivoOperacionalId : null,
  )
  const accounting = useRemote<ImportedFile>(
    summary ? '/importacoes/' + summary.arquivoContabilId : null,
  )
  const query = filterQuery(filters)
  query.set('pagina', String(page))
  query.set('tamanhoPagina', '20')
  const items = useRemote<Page<Item>>(
    summary?.status === 'CONCLUIDA'
      ? `/conciliacoes/${id}/itens?${query}`
      : null,
    revision,
  )
  function apply(event: FormEvent) {
    event.preventDefault()
    if (
      draft.dataInicial &&
      draft.dataFinal &&
      draft.dataInicial > draft.dataFinal
    ) {
      setError('A data inicial deve ser anterior ou igual à data final.')
      return
    }
    if (draft.idOperacao && !draft.idOperacao.trim()) {
      setError('Informe um identificador diferente de espaços.')
      return
    }
    setError('')
    setFilters({ ...draft })
    setPage(1)
    setSelected(null)
  }
  async function download() {
    if (exporting) return
    setExporting(true)
    setError('')
    try {
      await exportCsv(id, filters)
    } catch (error) {
      setError(message(error))
    } finally {
      setExporting(false)
    }
  }
  return (
    <>
      <Heading
        title="Resultados"
        sub={
          summary
            ? `Execução ${summary.id} · ${instant(summary.criadaEm)}`
            : 'Consultando a execução…'
        }
      >
        <div className="fl-actions">
          <button
            className="fl-button"
            disabled={execution.loading || items.loading}
            onClick={() => setRevision((value) => value + 1)}
          >
            <RefreshCw aria-hidden />
            Atualizar
          </button>
          <button
            className="fl-button"
            disabled={!summary || summary.status !== 'CONCLUIDA' || exporting}
            onClick={download}
          >
            <Download aria-hidden />
            {exporting ? 'Exportando…' : 'Exportar CSV'}
          </button>
        </div>
      </Heading>
      {execution.loading && <Loading />}
      {execution.error && <Notice kind="error">{execution.error}</Notice>}
      {error && <Notice kind="error">{error}</Notice>}
      {summary && summary.status !== 'CONCLUIDA' && (
        <Notice kind={summary.status === 'FALHOU' ? 'error' : 'info'}>
          <Badge value={summary.status} /> Esta execução não possui resultados
          definitivos.{' '}
          {summary.status === 'FALHOU' &&
            'Inicie uma nova tentativa com os arquivos de origem.'}
        </Notice>
      )}
      {summary?.status === 'CONCLUIDA' && (
        <>
          <div className="fl-filesummary">
            <span>
              <strong>Operacional:</strong>{' '}
              {operational.data?.nomeOriginal || summary.arquivoOperacionalId}
            </span>
            <span>
              <strong>Contábil:</strong>{' '}
              {accounting.data?.nomeOriginal || summary.arquivoContabilId}
            </span>
            <span>
              <strong>Regras:</strong> {summary.versaoRegras}
            </span>
          </div>
          {(operational.error || accounting.error) && (
            <Notice kind="error">
              Não foi possível consultar os metadados de um dos arquivos.
            </Notice>
          )}
          <div className="fl-resultmetrics">
            <span>
              <strong>{summary.totalItens}</strong>itens
            </span>
            <span>
              <strong>{summary.paresConciliados}</strong>conciliados
            </span>
            <span>
              <strong>
                {summary.paresDivergentes +
                  summary.semRegistroContabil +
                  summary.semOperacaoFinanceira}
              </strong>
              com divergência
            </span>
            <span>
              <strong>{rate(summary.taxaConciliacao)}</strong>taxa
            </span>
          </div>
          <form className="fl-panel" onSubmit={apply}>
            <div className="fl-filters">
              <label>
                Identificador exato
                <input
                  value={draft.idOperacao}
                  onChange={(event) =>
                    setDraft({ ...draft, idOperacao: event.target.value })
                  }
                  placeholder="Ex.: OP004"
                />
              </label>
              <label>
                Resultado
                <select
                  className="fl-select"
                  value={draft.resultado}
                  onChange={(event) =>
                    setDraft({ ...draft, resultado: event.target.value })
                  }
                >
                  <option value="">Todos os resultados</option>
                  {results.map((value) => (
                    <option value={value} key={value}>
                      {labels[value]}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Situação da análise
                <select
                  className="fl-select"
                  value={draft.situacaoAnalise}
                  onChange={(event) =>
                    setDraft({ ...draft, situacaoAnalise: event.target.value })
                  }
                >
                  <option value="">Todas as situações</option>
                  {statuses.map((value) => (
                    <option value={value} key={value}>
                      {labels[value]}
                    </option>
                  ))}
                </select>
              </label>
              {(['dataOperacao', 'dataInicial', 'dataFinal'] as const).map(
                (key, index) => (
                  <label key={key}>
                    {['Data exata', 'Data inicial', 'Data final'][index]}
                    <input
                      type="date"
                      value={draft[key]}
                      onChange={(event) =>
                        setDraft({ ...draft, [key]: event.target.value })
                      }
                    />
                  </label>
                ),
              )}
            </div>
            <div className="fl-actions">
              <span className="fl-small">
                Data de referência: operacional; na ausência, contábil.
              </span>
              <div className="fl-actions">
                <button
                  className="fl-button"
                  type="button"
                  onClick={() => {
                    setDraft({ ...emptyFilters })
                    setFilters({ ...emptyFilters })
                    setPage(1)
                    setSelected(null)
                    setError('')
                  }}
                >
                  Limpar filtros
                </button>
                <button className="fl-button fl-primary" type="submit">
                  Aplicar filtros
                </button>
              </div>
            </div>
            <p className="fl-small">
              A exportação usa os filtros aplicados e inclui todas as páginas.
            </p>
          </form>
          <div className={selected ? 'fl-split' : ''}>
            <section className="fl-panel">
              <div className="fl-panelhead">
                <h2>Itens de conciliação</h2>
                <span className="fl-small" aria-live="polite">
                  {items.data ? `${items.data.total} itens encontrados` : ''}
                </span>
              </div>
              {items.loading && <Loading />}
              {items.error && <Notice kind="error">{items.error}</Notice>}
              {items.data && (
                <>
                  <div className="fl-scroll">
                    <table className="fl-table">
                      <thead>
                        <tr>
                          <th>Operação</th>
                          <th>Operacional</th>
                          <th>Contábil</th>
                          <th>Resultado / análise</th>
                        </tr>
                      </thead>
                      <tbody>
                        {items.data.itens.map((item) => (
                          <tr
                            className={`fl-record ${selected === item.id ? 'selected' : ''}`}
                            key={item.id}
                          >
                            <td>
                              <button
                                className="fl-link"
                                aria-label={'Abrir operação ' + item.idOperacao}
                                onClick={() => setSelected(item.id)}
                              >
                                {item.idOperacao}
                              </button>
                            </td>
                            <td>
                              {amount(item.valorOperacional)}
                              <span className="fl-date">
                                {date(item.dataOperacional)}
                              </span>
                            </td>
                            <td>
                              {amount(item.valorContabil)}
                              <span className="fl-date">
                                {date(item.dataContabil)}
                              </span>
                            </td>
                            <td>
                              <Badge value={item.resultado} />
                              <div className="fl-small">
                                {item.divergencias
                                  .map((value) => labels[value])
                                  .join(' e ')}{' '}
                                {item.situacaoAnalise && (
                                  <Badge value={item.situacaoAnalise} />
                                )}
                              </div>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                    {items.data.itens.length === 0 && (
                      <p className="fl-empty">
                        Nenhum item corresponde aos filtros.
                      </p>
                    )}
                  </div>
                  <Pagination
                    page={page}
                    total={items.data.total}
                    change={(value) => {
                      setPage(value)
                      setSelected(null)
                    }}
                  />
                </>
              )}
            </section>
            {selected && (
              <ItemDetails
                key={selected}
                executionId={id}
                itemId={selected}
                onClose={() => setSelected(null)}
                onChanged={() => setRevision((value) => value + 1)}
              />
            )}
          </div>
        </>
      )}
    </>
  )
}
