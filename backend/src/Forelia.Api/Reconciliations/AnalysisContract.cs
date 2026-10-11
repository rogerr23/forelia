using Forelia.Api.Data;

namespace Forelia.Api.Reconciliations;

public sealed record AnalysisRequest(string? Situacao);
public sealed record ObservationRequest(string? Texto, string? Analista);
public sealed record ObservationResponse(Guid Id, Guid ItemId, string Texto, string Analista,
    DateTime CriadaEm, bool IdentidadeAutenticada, string AvisoIdentidade)
{
    public static ObservationResponse From(AnalystObservation x) => new(x.Id, x.ReconciliationItemId,
        x.Text, x.Analyst, x.CreatedAt, false, "Identidade não autenticada");
}
