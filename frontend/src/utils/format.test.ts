import { describe, expect, it } from 'vitest'
import { amount, date } from './format'

describe('Formatação financeira e datas', () => {
  it('distingue ausência, zero e negativo sem arredondar', () => {
    expect(amount(null)).toBe('—')
    expect(amount('0')).toBe('0,00')
    expect(amount('-12345678901234567890.01')).toBe(
      '-12.345.678.901.234.567.890,01',
    )
    expect(amount('20.10')).toBe('20,10')
    expect(date('2026-10-04')).toBe('04/10/2026')
  })
})
