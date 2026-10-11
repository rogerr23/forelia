export interface Page<T> {
  pagina: number
  tamanhoPagina: number
  total: number
  itens: T[]
}
export interface ImportedFile {
  id: string
  fonte: string
  nomeOriginal: string
  totalRegistros: number
  tamanhoBytes: number
  importadoEm: string
}
export interface Execution {
  id: string
  arquivoOperacionalId: string
  arquivoContabilId: string
  versaoRegras: string
  status: string
  criadaEm: string
  finalizadaEm: string | null
  codigoErro: string | null
  totalRegistrosOrigem: number
  totalItens: number
  paresConciliados: number
  paresDivergentes: number
  semRegistroContabil: number
  semOperacaoFinanceira: number
  taxaConciliacao: number
}
export interface Item {
  id: string
  idOperacao: string
  valorOperacional: string | null
  valorContabil: string | null
  dataOperacional: string | null
  dataContabil: string | null
  resultado: string
  divergencias: string[]
  situacaoAnalise: string | null
}
export interface Observation {
  id: string
  texto: string
  analista: string
  criadaEm: string
  avisoIdentidade: string
  identidadeAutenticada: boolean
}
export interface Dashboard {
  execucoes: {
    total: number
    pendentes: number
    processando: number
    concluidas: number
    falhas: number
  }
  conciliacaoSelecionada: Execution | null
  indicadores: Omit<
    Execution,
    | 'id'
    | 'arquivoOperacionalId'
    | 'arquivoContabilId'
    | 'versaoRegras'
    | 'status'
    | 'criadaEm'
    | 'finalizadaEm'
    | 'codigoErro'
  > & { itensComDivergencia: number }
  distribuicaoDivergencias: { tipo: string; ocorrencias: number }[]
  execucoesRecentes: Execution[]
}
export interface CsvError {
  linha: number | null
  campo: string | null
  codigo: string
  mensagem: string
}
export interface ErrorBody {
  mensagem?: string
  title?: string
  detail?: string
  codigo?: string
  erros?: CsvError[]
  importacaoExistenteId?: string
}

export interface Filters {
  idOperacao: string
  resultado: string
  situacaoAnalise: string
  dataOperacao: string
  dataInicial: string
  dataFinal: string
}
