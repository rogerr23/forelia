export function Pagination({
  page,
  total,
  change,
}: {
  page: number
  total: number
  change: (page: number) => void
}) {
  const last = Math.max(1, Math.ceil(total / 20))
  return (
    <div className="fl-actions fl-pagination">
      <span className="fl-small">
        {total} registros · Página {page} de {last}
      </span>
      <div className="fl-actions">
        <button
          className="fl-button"
          disabled={page <= 1}
          onClick={() => change(page - 1)}
        >
          Anterior
        </button>
        <button
          className="fl-button"
          disabled={page >= last}
          onClick={() => change(page + 1)}
        >
          Próxima
        </button>
      </div>
    </div>
  )
}
