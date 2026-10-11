using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;

namespace Forelia.Api.Imports;

public sealed partial class CsvValidator
{
    private static readonly string[] Header = ["id_operacao", "data_operacao", "valor"];

    public CsvValidation Validate(byte[] content, CancellationToken cancellationToken = default)
    {
        var records = new List<CsvRecord>();
        var errors = new List<ImportError>();
        if (content.Length > ImportContract.MaxFileBytes)
            return new([], [new(null, null, "ARQUIVO_MUITO_GRANDE", "O arquivo deve ter no máximo 10 MB (10.485.760 bytes).")]);

        string text;
        try { text = new UTF8Encoding(false, true).GetString(content); }
        catch (DecoderFallbackException)
        {
            return new([], [new(null, null, "CODIFICACAO_INVALIDA", "Use um arquivo UTF-8, com ou sem BOM.")]);
        }
        if (text.StartsWith('\uFEFF')) text = text[1..];

        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = ";",
            IgnoreBlankLines = false,
            TrimOptions = TrimOptions.Trim,
            ExceptionMessagesContainRawData = false
        };
        using var reader = new StringReader(text);
        using var parser = new CsvParser(reader, configuration);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var hasHeader = false;
        var line = 1;
        var rowCount = 0;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                line = parser.RawRow + 1;
                if (!parser.Read()) break;
                if (string.IsNullOrWhiteSpace(parser.RawRecord)) continue;
                var fields = parser.Record!.Select(field => field.Trim()).ToArray();
                if (!hasHeader)
                {
                    if (!fields.SequenceEqual(Header))
                        return new([], [new(line, null, "CABECALHO_INVALIDO", "O cabeçalho deve ser id_operacao;data_operacao;valor, nessa ordem.")]);
                    hasHeader = true;
                    continue;
                }

                rowCount++;
                if (fields.Length != 3)
                {
                    errors.Add(new(line, null, "ESTRUTURA_INVALIDA", "A linha deve conter exatamente três colunas."));
                    continue;
                }
                var errorCount = errors.Count;
                for (var index = 0; index < fields.Length; index++)
                {
                    if (fields[index].Length == 0)
                        errors.Add(new(line, Header[index], "CAMPO_OBRIGATORIO", "O campo é obrigatório."));
                    else if (fields[index].Contains('\0'))
                        errors.Add(new(line, Header[index], "CAMPO_INVALIDO", "O campo contém um caractere nulo inválido."));
                }

                if (fields[0].Length > 0 && !fields[0].Contains('\0'))
                {
                    if (seen.TryGetValue(fields[0], out var firstLine))
                        errors.Add(new(line, "id_operacao", "REGISTRO_DUPLICADO", $"O identificador já aparece na linha {firstLine}."));
                    else seen.Add(fields[0], line);
                }

                var date = default(DateOnly);
                if (fields[1].Length > 0 &&
                    (!DateFormat().IsMatch(fields[1]) || !DateOnly.TryParseExact(fields[1], "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out date)))
                    errors.Add(new(line, "data_operacao", "DATA_INVALIDA", "Use uma data válida no formato yyyy-MM-dd."));

                var amount = 0m;
                if (fields[2].Length > 0)
                {
                    if (!AmountFormat().IsMatch(fields[2]))
                        errors.Add(new(line, "valor", "VALOR_INVALIDO", "Use um número com ponto decimal e até duas casas, sem separador de milhar."));
                    else if (!decimal.TryParse(fields[2], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                                 CultureInfo.InvariantCulture, out amount) ||
                             NormalizeAmount(fields[2]) != amount.ToString("0.##", CultureInfo.InvariantCulture))
                        errors.Add(new(line, "valor", "VALOR_FORA_DO_LIMITE", "O valor não pode ser representado exatamente pelo tipo decimal."));
                }
                if (errors.Count == errorCount) records.Add(new(line, fields[0], date, amount));
            }
        }
        catch (CsvHelperException)
        {
            errors.Add(new(line, null, "CSV_INVALIDO", "A estrutura CSV contém aspas ou campos malformados."));
        }

        if (!hasHeader && errors.Count == 0)
            errors.Add(new(null, null, "CABECALHO_INVALIDO", "O arquivo deve conter o cabeçalho id_operacao;data_operacao;valor."));
        else if (hasHeader && rowCount == 0 && errors.Count == 0)
            errors.Add(new(null, null, "SEM_REGISTROS", "O arquivo deve conter pelo menos um registro."));

        // Uma importação rejeitada nunca entrega registros elegíveis para persistência.
        return new(errors.Count == 0 ? records : [], errors);
    }

    private static string NormalizeAmount(string value)
    {
        var negative = value.StartsWith('-');
        var parts = value.TrimStart('+', '-').Split('.');
        var integer = parts[0].TrimStart('0');
        if (integer.Length == 0) integer = "0";
        var fraction = parts.Length == 2 ? parts[1].TrimEnd('0') : "";
        var sign = negative && (integer != "0" || fraction.Length > 0) ? "-" : "";
        return sign + integer + (fraction.Length > 0 ? "." + fraction : "");
    }

    [GeneratedRegex(@"\A[0-9]{4}-[0-9]{2}-[0-9]{2}\z", RegexOptions.NonBacktracking)]
    private static partial Regex DateFormat();

    [GeneratedRegex(@"\A[+-]?[0-9]+(?:\.[0-9]{1,2})?\z", RegexOptions.NonBacktracking)]
    private static partial Regex AmountFormat();
}
