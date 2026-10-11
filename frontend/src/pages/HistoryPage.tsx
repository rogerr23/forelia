import { useState } from 'react'
import { useRemote } from '../hooks/useRemote'
import { ExecutionTable } from '../components/ExecutionTable'
import { RefreshCw } from 'lucide-react'
import type { Execution, Page } from '../api/types'
import { Heading } from '../components/Heading'
import { Loading } from '../components/Loading'
import { Notice } from '../components/Notice'
import { Pagination } from '../components/Pagination'

export function HistoryPage() {
  const [page, setPage] = useState(1)
  const [revision, setRevision] = useState(0)
  const remote = useRemote<Page<Execution>>(
    `/conciliacoes?pagina=${page}&tamanhoPagina=20`,
    revision,
  )
  return (
    <>
      <Heading
        title="Histórico"
        sub="Execuções e resultados anteriores permanecem disponíveis."
      >
        <button
          className="fl-button"
          disabled={remote.loading}
          onClick={() => setRevision((value) => value + 1)}
        >
          <RefreshCw aria-hidden />
          Atualizar
        </button>
      </Heading>
      {remote.loading && <Loading />}
      {remote.error && <Notice kind="error">{remote.error}</Notice>}
      {remote.data && (
        <section className="fl-panel">
          <ExecutionTable items={remote.data.itens} />
          <Pagination page={page} total={remote.data.total} change={setPage} />
        </section>
      )}
    </>
  )
}
