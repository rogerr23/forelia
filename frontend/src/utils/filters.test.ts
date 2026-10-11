import { describe, expect, it } from 'vitest'
import { emptyFilters, filterQuery } from './filters'

describe('Parâmetros dos filtros', () => {
  it('omite filtros desativados e preserva exatamente o identificador', () => {
    const params = filterQuery({
      ...emptyFilters,
      idOperacao: ' 00Ab C ',
      dataInicial: '2026-10-01',
      dataFinal: '2026-10-04',
    })
    expect(params.get('idOperacao')).toBe(' 00Ab C ')
    expect(params.has('resultado')).toBe(false)
    expect(params.has('pagina')).toBe(false)
    expect(params.get('dataFinal')).toBe('2026-10-04')
  })
})
