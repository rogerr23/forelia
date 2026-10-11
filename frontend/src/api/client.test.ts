import { afterEach, describe, expect, it, vi } from 'vitest'
import { api, ApiError, importFile, parseResponse } from './client'
import { amount } from '../utils/format'
afterEach(() => vi.unstubAllGlobals())
describe('Valores financeiros', () => {
  it('preserva centavos e valores fora da precisão de Number desde a resposta JSON', () => {
    const item = parseResponse(
      '{"valorOperacional":792281625142643375935439503.35,"valorContabil":9007199254740991.99,"total":2,"taxaConciliacao":44.44}',
    ) as Record<string, unknown>
    expect(item.valorOperacional).toBe('792281625142643375935439503.35')
    expect(item.valorContabil).toBe('9007199254740991.99')
    expect(item.total).toBe(2)
    expect(item.taxaConciliacao).toBe(44.44)
    expect(amount(item.valorOperacional as string)).toBe(
      '792.281.625.142.643.375.935.439.503,35',
    )
  })
})
describe('Integração com a API', () => {
  it('reutiliza a importação indicada no 409 e envia um único campo multipart', async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(
        new Response('{"importacaoExistenteId":"id-existente"}', {
          status: 409,
        }),
      )
      .mockResolvedValueOnce(
        new Response('{"id":"id-existente","totalRegistros":8}'),
      )
    vi.stubGlobal('fetch', fetch)
    const result = await importFile(
      'operacional',
      new File(['id_operacao;data_operacao;valor'], 'ficticio.csv'),
    )
    expect(result.reused).toBe(true)
    expect(result.file.id).toBe('id-existente')
    expect(fetch.mock.calls[1][0]).toBe('/api/importacoes/id-existente')
    expect([...fetch.mock.calls[0][1].body.keys()]).toEqual(['arquivo'])
  })
  it('preserva os erros por linha da rejeição sem repetir a solicitação', async () => {
    const fetch = vi
      .fn()
      .mockResolvedValue(
        new Response(
          '{"erros":[{"linha":4,"campo":"valor","codigo":"VALOR_INVALIDO","mensagem":"Valor inválido"}]}',
          { status: 422 },
        ),
      )
    vi.stubGlobal('fetch', fetch)
    await expect(
      importFile('contabil', new File(['invalido'], 'ficticio.csv')),
    ).rejects.toMatchObject({ status: 422, body: { erros: [{ linha: 4 }] } })
    expect(fetch).toHaveBeenCalledTimes(1)
  })
  it('não expõe detalhes técnicos retornados em uma falha do servidor', async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockResolvedValue(
          new Response('{"detail":"detalhe técnico fictício"}', {
            status: 500,
          }),
        ),
    )
    await expect(api('/dashboard')).rejects.toThrow(
      'Não foi possível concluir a solicitação. Verifique a API e tente novamente.',
    )
    expect(new ApiError(404, {}).message).toBe('Registro não encontrado.')
  })
})
