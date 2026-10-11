import { isLosslessNumber, parse } from 'lossless-json'

import type { ErrorBody, Filters, ImportedFile } from './types'
import { filterQuery } from '../utils/filters'

// Preserve o token decimal recebido do .NET: Number arredonda valores grandes.
export function parseResponse(text: string): unknown {
  return parse(text, (key, value) =>
    isLosslessNumber(value)
      ? key === 'valorOperacional' || key === 'valorContabil'
        ? value.value
        : Number(value.value)
      : value,
  )
}
export class ApiError extends Error {
  readonly status: number
  readonly body: ErrorBody
  constructor(status: number, body: ErrorBody) {
    super(
      status >= 500
        ? 'Não foi possível concluir a solicitação. Verifique a API e tente novamente.'
        : body.mensagem ||
            body.detail ||
            body.title ||
            (status === 404
              ? 'Registro não encontrado.'
              : 'Não foi possível concluir a solicitação.'),
    )
    this.status = status
    this.body = body
  }
}
async function response(path: string, init?: RequestInit): Promise<Response> {
  let result: Response
  try {
    result = await fetch('/api' + path, init)
  } catch (error) {
    if (error instanceof Error && error.name === 'AbortError') throw error
    throw new Error(
      'Não foi possível acessar a API. Confira se o backend está em execução.',
    )
  }
  if (!result.ok) {
    const text = await result.text()
    let body: ErrorBody = {}
    try {
      body = parseResponse(text) as ErrorBody
    } catch {
      /* Pode ser uma resposta vazia do proxy. */
    }
    throw new ApiError(result.status, body)
  }
  return result
}
export async function api<T>(path: string, init?: RequestInit): Promise<T> {
  return parseResponse(await (await response(path, init)).text()) as T
}
export function json(method: string, body: unknown): RequestInit {
  return {
    method,
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  }
}
export async function importFile(
  source: string,
  file: File,
): Promise<{ file: ImportedFile; reused: boolean }> {
  const form = new FormData()
  form.append('arquivo', file)
  try {
    return {
      file: await api<ImportedFile>('/importacoes/' + source, {
        method: 'POST',
        body: form,
      }),
      reused: false,
    }
  } catch (error) {
    if (
      error instanceof ApiError &&
      error.status === 409 &&
      error.body.importacaoExistenteId
    ) {
      return {
        file: await api<ImportedFile>(
          '/importacoes/' + error.body.importacaoExistenteId,
        ),
        reused: true,
      }
    }
    throw error
  }
}
export async function exportCsv(id: string, filters: Filters): Promise<void> {
  const result = await response(
    `/conciliacoes/${id}/exportacao?${filterQuery(filters)}`,
  )
  const url = URL.createObjectURL(await result.blob())
  const link = document.createElement('a')
  link.href = url
  link.download = `conciliacao-${id}.csv`
  document.body.append(link)
  link.click()
  link.remove()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}
