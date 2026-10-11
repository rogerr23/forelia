import { useRef, useState } from 'react'
import {
  ArrowRight,
  FileCheck2,
  FileUp,
  ShieldCheck,
  Upload,
} from 'lucide-react'
import { api, ApiError, importFile, json } from '../api/client'
import type { CsvError, Execution, ImportedFile } from '../api/types'
import { Badge } from '../components/Badge'
import { Heading } from '../components/Heading'
import { Notice } from '../components/Notice'
import { resultLink } from '../utils/format'
type Source = 'operacional' | 'contabil'
interface SourceState {
  file: File | null
  imported: ImportedFile | null
  reused: boolean
  errors: CsvError[]
  error: string
}
const fresh = (): SourceState => ({
  file: null,
  imported: null,
  reused: false,
  errors: [],
  error: '',
})
export function ImportPage() {
  const [sources, setSources] = useState<Record<Source, SourceState>>({
    operacional: fresh(),
    contabil: fresh(),
  })
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const lock = useRef(false)
  const ready = !!sources.operacional.file && !!sources.contabil.file
  const imported = !!sources.operacional.imported && !!sources.contabil.imported
  function select(source: Source, file: File | null) {
    setError('')
    setSources((current) => ({
      ...current,
      [source]: {
        ...fresh(),
        file,
        error:
          file && file.size > 10485760
            ? 'O arquivo deve ter no máximo 10 MB (10.485.760 bytes).'
            : '',
      },
    }))
  }
  async function validate() {
    if (lock.current) return
    lock.current = true
    setBusy(true)
    setError('')
    try {
      // Cada fonte possui sua própria importação atômica. Uma fonte rejeitada não desfaz a outra.
      for (const source of ['operacional', 'contabil'] as const) {
        const entry = sources[source]
        if (!entry.file || entry.imported || entry.file.size > 10485760)
          continue
        try {
          const result = await importFile(source, entry.file)
          setSources((current) => ({
            ...current,
            [source]: {
              ...entry,
              imported: result.file,
              reused: result.reused,
              errors: [],
              error: '',
            },
          }))
        } catch (error) {
          setSources((current) => ({
            ...current,
            [source]: {
              ...entry,
              error:
                error instanceof Error ? error.message : 'Falha na importação.',
              errors: error instanceof ApiError ? error.body.erros || [] : [],
            },
          }))
        }
      }
    } finally {
      lock.current = false
      setBusy(false)
    }
  }
  async function reconcile() {
    if (lock.current || !imported) return
    lock.current = true
    setBusy(true)
    setError('')
    try {
      const execution = await api<Execution>(
        '/conciliacoes',
        json('POST', {
          arquivoOperacionalId: sources.operacional.imported!.id,
          arquivoContabilId: sources.contabil.imported!.id,
        }),
      )
      window.location.hash = resultLink(execution.id)
    } catch (error) {
      setError(
        error instanceof Error ? error.message : 'Não foi possível conciliar.',
      )
    } finally {
      lock.current = false
      setBusy(false)
    }
  }
  return (
    <>
      <Heading
        title="Nova conciliação"
        sub="Importe os arquivos, confira a validação e compare as duas fontes."
      />
      <div className="fl-stepper" aria-label="Etapas da conciliação">
        {['Arquivos de origem', 'Validação', 'Conciliação'].map(
          (label, index) => (
            <span
              className={`fl-step ${index === (imported ? 2 : ready ? 1 : 0) ? 'is-active' : ''}`}
              key={label}
            >
              <b>{index + 1}</b>
              {label}
            </span>
          ),
        )}
      </div>
      {error && <Notice kind="error">{error}</Notice>}
      <section className="fl-panel">
        <div className="fl-panelhead">
          <h2>Arquivos de origem</h2>
          <span className="fl-small">Um arquivo por fonte</span>
        </div>
        <div className="fl-uploadgrid">
          {(['operacional', 'contabil'] as const).map((source) => {
            const entry = sources[source],
              Icon = entry.imported ? FileCheck2 : FileUp
            return (
              <section
                className="fl-upload"
                key={source}
                aria-label={'Fonte ' + source}
              >
                <span className="fl-uploadmark">
                  <Icon aria-hidden />
                </span>
                <h2>
                  Fonte {source === 'operacional' ? 'operacional' : 'contábil'}
                </h2>
                <span className="fl-filename">
                  {entry.file?.name || 'Escolha o arquivo desta fonte'}
                </span>
                <span className="fl-small">
                  {entry.imported
                    ? `${entry.imported.totalRegistros} registros · importação disponível`
                    : 'CSV em UTF-8 · até 10 MB'}
                </span>
                <Badge
                  value={
                    entry.imported
                      ? 'DISPONIVEL'
                      : entry.file
                        ? 'SELECIONADO'
                        : 'AGUARDANDO'
                  }
                />
                <label className="fl-filelabel">
                  <Upload aria-hidden />
                  {entry.file ? 'Trocar arquivo' : 'Selecionar arquivo'}
                  <input
                    aria-label={'Selecionar arquivo ' + source}
                    type="file"
                    accept=".csv,text/csv"
                    disabled={busy}
                    onChange={(event) =>
                      select(source, event.target.files?.[0] || null)
                    }
                  />
                </label>
                {entry.reused && (
                  <span className="fl-small">
                    Conteúdo já importado: reutilizando a importação existente.
                  </span>
                )}
                {entry.error && (
                  <Notice kind="error">
                    {entry.errors.length
                      ? 'Arquivo rejeitado: nenhuma linha deste arquivo foi importada.'
                      : entry.error}
                  </Notice>
                )}
                {entry.errors.length > 0 && (
                  <div className="fl-scroll">
                    <table className="fl-error-table">
                      <thead>
                        <tr>
                          <th>Linha</th>
                          <th>Campo</th>
                          <th>Problema</th>
                        </tr>
                      </thead>
                      <tbody>
                        {entry.errors.map((error, index) => (
                          <tr key={index}>
                            <td>{error.linha ?? '—'}</td>
                            <td>{error.campo || '—'}</td>
                            <td>{error.mensagem}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </section>
            )
          })}
        </div>
        <Notice>
          Os dados de origem são preservados. Correções chegam por um novo
          arquivo e uma nova conciliação.
        </Notice>
        <div className="fl-actions">
          <span className="fl-small">
            {busy
              ? 'Aguarde a conclusão da solicitação.'
              : imported
                ? 'As duas importações estão disponíveis.'
                : 'Selecione as duas fontes para continuar.'}
          </span>
          <button
            className="fl-button fl-primary"
            disabled={
              busy ||
              !ready ||
              Object.values(sources).some(
                (entry) => entry.file && entry.file.size > 10485760,
              )
            }
            onClick={imported ? reconcile : validate}
          >
            {imported ? (
              <ArrowRight aria-hidden />
            ) : (
              <ShieldCheck aria-hidden />
            )}
            {busy
              ? 'Processando…'
              : imported
                ? 'Executar conciliação'
                : 'Validar e importar arquivos'}
          </button>
        </div>
      </section>
      <section className="fl-panel">
        <h2>Formato esperado</h2>
        <p className="fl-small">Cabeçalho: id_operacao;data_operacao;valor</p>
        <p className="fl-small">
          Datas: 2026-10-04 · Valores: 420.00 · Identificadores preservados como
          texto
        </p>
      </section>
    </>
  )
}
