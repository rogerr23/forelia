using Forelia.Api.Reconciliations;

namespace Forelia.Api.Dashboard;

public sealed record DashboardResponse(DashboardExecutionCounts Execucoes,
    ReconciliationResponse? ConciliacaoSelecionada, DashboardIndicators Indicadores,
    DashboardDivergenceCount[] DistribuicaoDivergencias, ReconciliationResponse[] ExecucoesRecentes);

public sealed record DashboardExecutionCounts(int Total, int Pendentes, int Processando, int Concluidas, int Falhas);

public sealed record DashboardIndicators(int TotalRegistrosOrigem, int TotalItens, int ParesConciliados,
    int ParesDivergentes, int SemRegistroContabil, int SemOperacaoFinanceira,
    int ItensComDivergencia, decimal TaxaConciliacao);

public sealed record DashboardDivergenceCount(string Tipo, int Ocorrencias);
