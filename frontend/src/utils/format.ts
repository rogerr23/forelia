export const labels: Record<string, string> = {
  CONCLUIDA: 'Concluída',
  FALHOU: 'Falhou',
  PROCESSANDO: 'Processando',
  PENDENTE: 'Pendente',
  EM_ANALISE: 'Em análise',
  ANALISADA: 'Analisada',
  CONCILIADO: 'Conciliado',
  DIVERGENTE: 'Divergente',
  SEM_REGISTRO_CONTABIL: 'Sem registro contábil',
  SEM_OPERACAO_FINANCEIRA: 'Sem operação financeira',
  VALOR: 'Diferença de valor',
  DATA: 'Diferença de data',
  AGUARDANDO: 'Aguardando arquivo',
  SELECIONADO: 'Selecionado',
  DISPONIVEL: 'Disponível para conciliar',
}
export function resultLink(id: string) {
  return '#resultados/' + id
}
export function amount(value: string | null): string {
  if (value === null) return '—'
  if (!/^-?\d+(\.\d{1,2})?$/.test(value)) return value
  const [integer, fraction = ''] = value.split('.')
  return `${integer.replace(/\B(?=(\d{3})+(?!\d))/g, '.')},${fraction.padEnd(2, '0')}`
}
export function date(value: string | null): string {
  return value ? value.split('-').reverse().join('/') : '—'
}
export function instant(value: string): string {
  return new Date(value).toLocaleString('pt-BR', {
    dateStyle: 'short',
    timeStyle: 'short',
  })
}
export function rate(value: number): string {
  return value.toLocaleString('pt-BR', { maximumFractionDigits: 2 }) + '%'
}
