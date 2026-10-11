import { useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { MessageSquarePlus } from 'lucide-react'
import { api, json } from '../../api/client'
import type { Item, Observation, Page } from '../../api/types'
import { amount, date, instant, labels } from '../../utils/format'
import { useRemote } from '../../hooks/useRemote'
import { Badge } from '../../components/Badge'
import { Loading } from '../../components/Loading'
import { Notice } from '../../components/Notice'
import { Pagination } from '../../components/Pagination'

const statuses = ['PENDENTE', 'EM_ANALISE', 'ANALISADA']
const message = (error: unknown) =>
  error instanceof Error
    ? error.message
    : 'Não foi possível concluir a solicitação.'

export function ItemDetails({
  executionId,
  itemId,
  onClose,
  onChanged,
}: {
  executionId: string
  itemId: string
  onClose: () => void
  onChanged: () => void
}) {
  const path = `/conciliacoes/${executionId}/itens/${itemId}`
  const [revision, setRevision] = useState(0)
  const [noteRevision, setNoteRevision] = useState(0)
  const [page, setPage] = useState(1)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [text, setText] = useState('')
  const [analyst, setAnalyst] = useState('')
  const lock = useRef(false)
  const remote = useRemote<Item>(path, revision)
  const item = remote.data
  const notes = useRemote<Page<Observation>>(
    item?.situacaoAnalise
      ? `${path}/observacoes?pagina=${page}&tamanhoPagina=20`
      : null,
    noteRevision,
  )
  async function changeStatus(status: string) {
    if (lock.current) return
    lock.current = true
    setBusy(true)
    setError('')
    try {
      await api<Item>(path + '/analise', json('PATCH', { situacao: status }))
      setRevision((value) => value + 1)
      onChanged()
    } catch (error) {
      setError(message(error))
    } finally {
      lock.current = false
      setBusy(false)
    }
  }
  async function addObservation(event: FormEvent) {
    event.preventDefault()
    if (lock.current) return
    if (!text.trim() || !analyst.trim()) {
      setError(
        'Informe texto e identificação do analista diferentes de espaços.',
      )
      return
    }
    lock.current = true
    setBusy(true)
    setError('')
    try {
      await api<Observation>(
        path + '/observacoes',
        json('POST', { texto: text, analista: analyst }),
      )
      setText('')
      setNoteRevision((value) => value + 1)
      if (notes.data)
        setPage(Math.max(1, Math.ceil((notes.data.total + 1) / 20)))
    } catch (error) {
      setError(message(error))
    } finally {
      lock.current = false
      setBusy(false)
    }
  }
  return (
    <section className="fl-panel fl-detail" aria-label="Detalhes da operação">
      <div className="fl-panelhead">
        <div>
          <h2>{item?.idOperacao || 'Operação'}</h2>
          <span className="fl-small">Detalhes da operação</span>
        </div>
        <button className="fl-button" disabled={busy} onClick={onClose}>
          Fechar
        </button>
      </div>
      {remote.loading && <Loading />}
      {remote.error && <Notice kind="error">{remote.error}</Notice>}
      {error && <Notice kind="error">{error}</Notice>}
      {item && (
        <>
          <Badge value={item.resultado} />
          <table className="fl-data">
            <thead>
              <tr>
                <th>Comparação</th>
                <th>Operacional</th>
                <th>Contábil</th>
              </tr>
            </thead>
            <tbody>
              <tr>
                <td>Valor</td>
                <td
                  className={
                    item.divergencias.includes('VALOR') ? 'fl-highlight' : ''
                  }
                >
                  {amount(item.valorOperacional)}
                </td>
                <td
                  className={
                    item.divergencias.includes('VALOR') ? 'fl-highlight' : ''
                  }
                >
                  {amount(item.valorContabil)}
                </td>
              </tr>
              <tr>
                <td>Data</td>
                <td
                  className={
                    item.divergencias.includes('DATA') ? 'fl-highlight' : ''
                  }
                >
                  {date(item.dataOperacional)}
                </td>
                <td
                  className={
                    item.divergencias.includes('DATA') ? 'fl-highlight' : ''
                  }
                >
                  {date(item.dataContabil)}
                </td>
              </tr>
            </tbody>
          </table>
          {!item.situacaoAnalise ? (
            <p className="fl-small">Este item não requer análise.</p>
          ) : (
            <>
              <label>
                Situação da análise
                <select
                  className="fl-select"
                  value={item.situacaoAnalise}
                  disabled={busy}
                  onChange={(event) => changeStatus(event.target.value)}
                >
                  {statuses.map((value) => (
                    <option value={value} key={value}>
                      {labels[value]}
                    </option>
                  ))}
                </select>
              </label>
              <p className="fl-small">
                Analisar não altera o resultado financeiro.
              </p>
              <h2 className="fl-notes-title">Observações</h2>
              {notes.loading && <Loading />}
              {notes.error && <Notice kind="error">{notes.error}</Notice>}
              {notes.data?.itens.map((note) => (
                <article className="fl-notes" key={note.id}>
                  <strong>{note.analista}</strong>
                  <div className="fl-small">
                    {instant(note.criadaEm)} · {note.avisoIdentidade}
                  </div>
                  <p>{note.texto}</p>
                </article>
              ))}
              {notes.data?.total === 0 && (
                <p className="fl-small">Nenhuma observação registrada.</p>
              )}
              {notes.data && notes.data.total > 20 && (
                <Pagination
                  page={page}
                  total={notes.data.total}
                  change={setPage}
                />
              )}
              <form onSubmit={addObservation}>
                <label>
                  Identificação do analista
                  <input
                    required
                    maxLength={100}
                    value={analyst}
                    onChange={(event) => setAnalyst(event.target.value)}
                    disabled={busy}
                    placeholder="Seu identificador"
                  />
                </label>
                <label className="fl-noteinput">
                  Nova observação
                  <textarea
                    required
                    maxLength={2000}
                    value={text}
                    onChange={(event) => setText(event.target.value)}
                    disabled={busy}
                    placeholder="Descreva a análise realizada"
                  />
                </label>
                <p className="fl-small">
                  Identidade não autenticada. Observações são imutáveis;
                  correções devem ser novas observações.
                </p>
                <button
                  className="fl-button fl-primary fl-addnote"
                  type="submit"
                  disabled={busy}
                >
                  <MessageSquarePlus aria-hidden />
                  {busy ? 'Salvando…' : 'Adicionar observação'}
                </button>
              </form>
            </>
          )}
        </>
      )}
    </section>
  )
}
