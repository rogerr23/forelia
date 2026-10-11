using Forelia.Api.Data;
using Forelia.Api.Imports;
using Forelia.Api.Reconciliations;

namespace Forelia.Api.Tests;

public sealed class ReconciliationEngineTests
{
    [Fact]
    public void BaseExamplesPreserveBothDivergencesAndCountEachPairOnce()
    {
        var execution = Calculate("01_base/operacional.csv", "01_base/contabil.csv");
        Assert.Equal(16, execution.SourceRecords);
        Assert.Equal(9, execution.Items.Count);
        Assert.Equal(4, execution.MatchedPairs);
        Assert.Equal(3, execution.DivergentPairs);
        Assert.Equal(1, execution.MissingAccounting);
        Assert.Equal(1, execution.MissingOperational);
        Assert.Equal(44.44m, execution.ReconciliationRate);
        var item = Assert.Single(execution.Items, x => x.ExternalId == "OP004");
        Assert.Equal("DIVERGENTE", item.Result);
        Assert.Equal(new[] { "VALOR", "DATA" }, ReconciliationItemResponse.From(item).Divergencias);
        Assert.Contains(execution.Items, x => x.ExternalId == "OP005" && x.Result == "SEM_REGISTRO_CONTABIL" && x.AccountingAmount is null);
        Assert.Contains(execution.Items, x => x.ExternalId == "OP009" && x.Result == "SEM_OPERACAO_FINANCEIRA" && x.OperationalAmount is null);
        Assert.Contains(execution.Items, x => x.ExternalId == "OP006" && x.Result == "CONCILIADO" && x.OperationalAmount < 0);
        Assert.Contains(execution.Items, x => x.ExternalId == "OP007" && x.Result == "CONCILIADO" && x.OperationalAmount == 0);
    }

    [Theory]
    [InlineData("01_base/operacional.csv", "02_correcao/contabil_corrigido.csv", 8)]
    [InlineData("04_normalizacao/operacional_com_espacos.csv", "04_normalizacao/contabil_referencia.csv", 4)]
    public void CorrectedAndNormalizedExamplesReconcileFully(string operational, string accounting, int pairs)
    {
        var execution = Calculate(operational, accounting);
        Assert.Equal(pairs, execution.MatchedPairs);
        Assert.Equal(pairs, execution.Items.Count);
        Assert.Equal(0, execution.DivergentPairs);
        Assert.Equal(100m, execution.ReconciliationRate);
    }

    [Fact]
    public void IdentifiersAreCaseSensitiveAndPreserveZerosAndAmountsCompareExactly()
    {
        var execution = new Reconciliation();
        var date = new DateOnly(2026, 10, 1);
        ReconciliationEngine.Calculate(execution,
            [new() { ExternalId = "OP001", Amount = 1, OperationDate = date },
             new() { ExternalId = "op2", Amount = decimal.MaxValue, OperationDate = date }],
            [new() { ExternalId = "OP1", Amount = 1, OperationDate = date },
             new() { ExternalId = "OP2", Amount = 1, OperationDate = date },
             new() { ExternalId = "op2", Amount = decimal.MinValue, OperationDate = date.AddDays(1) }]);
        Assert.Equal(4, execution.Items.Count);
        Assert.Equal(1, execution.DivergentPairs);
        Assert.Equal(3, execution.MissingAccounting + execution.MissingOperational);
        Assert.Equal(0m, execution.ReconciliationRate);
    }

    [Fact]
    public void EmptyInputHasZeroRate()
    {
        var execution = new Reconciliation();
        ReconciliationEngine.Calculate(execution, [], []);
        Assert.Empty(execution.Items);
        Assert.Equal(0m, execution.ReconciliationRate);
    }

    private static Reconciliation Calculate(string operational, string accounting)
    {
        var execution = new Reconciliation();
        ReconciliationEngine.Calculate(execution, Read(operational), Read(accounting));
        return execution;
    }

    private static List<FinancialRecord> Read(string path)
    {
        var validation = new CsvValidator().Validate(CsvValidatorTests.Example(path));
        Assert.Empty(validation.Erros);
        return validation.Registros.Select(x => new FinancialRecord
        {
            ExternalId = x.IdOperacao, OperationDate = x.DataOperacao, Amount = x.Valor
        }).ToList();
    }
}
