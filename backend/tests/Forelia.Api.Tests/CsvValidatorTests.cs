using System.Text;
using Forelia.Api.Imports;

namespace Forelia.Api.Tests;

public sealed class CsvValidatorTests
{
    private readonly CsvValidator validator = new();

    [Theory]
    [InlineData("01_base/operacional.csv", 8)]
    [InlineData("01_base/contabil.csv", 8)]
    [InlineData("02_correcao/contabil_corrigido.csv", 8)]
    [InlineData("03_reimportacao/contabil_mesmo_conteudo_outro_nome.csv", 8)]
    [InlineData("04_normalizacao/operacional_com_espacos.csv", 4)]
    [InlineData("04_normalizacao/contabil_referencia.csv", 4)]
    public void AcceptsValidExamples(string path, int count)
    {
        var result = validator.Validate(Example(path));
        Assert.Empty(result.Erros);
        Assert.Equal(count, result.Registros.Count);
    }

    [Fact]
    public void NormalizesBomSpacesAndBlankLinesWithoutChangingIdentifiers()
    {
        var result = validator.Validate(Example("04_normalizacao/operacional_com_espacos.csv"));
        Assert.Equal(new CsvRecord(2, "OP001", new DateOnly(2026, 10, 1), 1500m), result.Registros[0]);
        Assert.Equal("OP000010", result.Registros[3].IdOperacao);
        Assert.Equal(5, result.Registros[3].Linha);
    }

    [Fact]
    public void RejectsBothExactAndConflictingDuplicates()
    {
        var result = validator.Validate(Example("05_duplicidades/operacional_duplicados.csv"));
        Assert.Empty(result.Registros);
        Assert.Collection(result.Erros,
            error => { Assert.Equal(10, error.Linha); Assert.Equal("REGISTRO_DUPLICADO", error.Codigo); Assert.Contains("linha 2", error.Mensagem); },
            error => { Assert.Equal(11, error.Linha); Assert.Equal("REGISTRO_DUPLICADO", error.Codigo); Assert.Contains("linha 3", error.Mensagem); });
    }

    [Fact]
    public void RejectsEveryIntentionalInvalidField()
    {
        var result = validator.Validate(Example("06_invalidos/operacional_campos_invalidos.csv"));
        Assert.Empty(result.Registros);
        Assert.Equal(10, result.Erros.Count);
        Assert.Equal(Enumerable.Range(3, 10), result.Erros.Select(error => error.Linha!.Value));
        Assert.Equal("data_operacao", result.Erros[0].Campo);
        Assert.Equal("CAMPO_OBRIGATORIO", result.Erros[1].Codigo);
        Assert.Equal("valor", result.Erros[2].Campo);
    }

    [Fact]
    public void ReportsMissingAndExtraColumns()
    {
        var result = validator.Validate(Example("06_invalidos/contabil_estrutura_invalida.csv"));
        Assert.Empty(result.Registros);
        Assert.Equal([3, 4], result.Erros.Select(error => error.Linha!.Value));
        Assert.All(result.Erros, error => Assert.Equal("ESTRUTURA_INVALIDA", error.Codigo));
    }

    [Theory]
    [InlineData("-75.50", "-75.50")]
    [InlineData("0.00", "0")]
    [InlineData("00042.50", "42.50")]
    [InlineData("-0.00", "0")]
    [InlineData("79228162514264337593543950335", "79228162514264337593543950335")]
    public void AcceptsExactDecimalValues(string input, string expected)
    {
        var result = ValidateRow($"OP001;2026-10-01;{input}");
        Assert.Empty(result.Erros);
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), Assert.Single(result.Registros).Valor);
    }

    [Theory]
    [InlineData("1.234")]
    [InlineData("1.230")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e2")]
    [InlineData("1,23")]
    [InlineData("1.234,56")]
    [InlineData("79228162514264337593543950336")]
    [InlineData("7922816251426433759354395033.51")]
    public void RejectsInvalidOrSilentlyRoundedAmounts(string amount)
    {
        var result = ValidateRow($"OP001;2026-10-01;{amount}");
        Assert.Empty(result.Registros);
        Assert.Equal("valor", Assert.Single(result.Erros).Campo);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\uFEFF")]
    [InlineData("id_operacao;data_operacao;valor\n")]
    [InlineData("valor;data_operacao;id_operacao\n1;2026-10-01;OP1")]
    [InlineData("id_operacao,data_operacao,valor\nOP1,2026-10-01,1")]
    public void RejectsEmptyFilesAndWrongHeaders(string csv)
    {
        Assert.False(validator.Validate(Encoding.UTF8.GetBytes(csv)).IsValid);
    }

    [Fact]
    public void PreservesPhysicalLineNumbersAfterBlankLines()
    {
        var result = validator.Validate(Encoding.UTF8.GetBytes("\n id_operacao ; data_operacao ; valor \n\n   \nOP1;2026-10-01;1\nOP2;2026-02-30;2"));
        Assert.Equal(6, Assert.Single(result.Erros).Linha);
    }

    [Fact]
    public void HandlesQuotedSeparatorsAndEscapedQuotes()
    {
        var result = ValidateRow("\"OP;\"\"1\";\"2026-10-01\";\"42.50\"");
        Assert.Empty(result.Erros);
        Assert.Equal("OP;\"1", Assert.Single(result.Registros).IdOperacao);
    }

    [Theory]
    [InlineData("\"OP1;2026-10-01;42.50")]
    [InlineData("OP\"1;2026-10-01;42.50")]
    public void RejectsMalformedQuotes(string row)
    {
        var result = ValidateRow(row);
        Assert.False(result.IsValid);
        Assert.Empty(result.Registros);
    }

    [Fact]
    public void RejectsInvalidUtf8AndOversizedFiles()
    {
        Assert.Equal("CODIFICACAO_INVALIDA", Assert.Single(validator.Validate([0xff, 0xfe]).Erros).Codigo);
        Assert.Equal("ARQUIVO_MUITO_GRANDE", Assert.Single(validator.Validate(new byte[ImportContract.MaxFileBytes + 1]).Erros).Codigo);
    }

    [Fact]
    public void DuplicateDetectionUsesTheTrimmedIdentifier()
    {
        var result = ValidateRow(" OP001 ;2026-10-01;1\nOP001;2026-10-01;2");
        Assert.Equal("REGISTRO_DUPLICADO", Assert.Single(result.Erros).Codigo);
    }

    private CsvValidation ValidateRow(string row) => validator.Validate(Encoding.UTF8.GetBytes("id_operacao;data_operacao;valor\n" + row));
    internal static byte[] Example(string path) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Examples", path));
}
