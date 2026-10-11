import type { Filters } from '../api/types'

export const emptyFilters: Filters = {
  idOperacao: '',
  resultado: '',
  situacaoAnalise: '',
  dataOperacao: '',
  dataInicial: '',
  dataFinal: '',
}
export function filterQuery(filters: Filters): URLSearchParams {
  return new URLSearchParams(
    Object.entries(filters).filter(([, value]) => value !== ''),
  )
}
