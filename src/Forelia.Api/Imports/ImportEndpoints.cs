using Forelia.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Forelia.Api.Imports;

public static class ImportEndpoints
{
    public static void MapImportEndpoints(this WebApplication app)
    {
        app.MapPost("/api/importacoes/{fonte}", ImportAsync)
            .WithName("ImportarCsv");
        app.MapGet("/api/importacoes/{id:guid}", async (Guid id, ForeliaDbContext database,
            CancellationToken cancellationToken) =>
        {
            // Não carregar o bytea ou todos os registros para consultar metadados.
            var result = await database.ImportedFiles.AsNoTracking().Where(x => x.Id == id)
                .Select(x => new ImportResponse(x.Id, x.Source.ToString(), x.OriginalName, x.ContentHash,
                    x.SizeBytes, x.RecordCount, x.ImportedAt, x.LayoutVersion))
                .SingleOrDefaultAsync(cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });
    }

    private static async Task<IResult> ImportAsync(string fonte, HttpRequest request,
        ImportService service, CancellationToken cancellationToken)
    {
        SourceType source;
        if (fonte.Equals("operacional", StringComparison.OrdinalIgnoreCase)) source = SourceType.OPERACIONAL;
        else if (fonte.Equals("contabil", StringComparison.OrdinalIgnoreCase)) source = SourceType.CONTABIL;
        else return Results.BadRequest(new { codigo = "FONTE_INVALIDA", mensagem = "Use operacional ou contabil." });

        if (request.ContentLength > ImportContract.MaxRequestBytes) return TooLarge();
        if (!request.HasFormContentType ||
            !request.ContentType!.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            return Results.Problem(statusCode: 415, title: "Envie o CSV como multipart/form-data no campo arquivo.");

        IFormCollection form;
        try { form = await request.ReadFormAsync(cancellationToken); }
        catch (BadHttpRequestException exception) when (exception.StatusCode == 413)
        { return TooLarge(); }
        catch (InvalidDataException exception) when (exception.Message.Contains("length limit", StringComparison.OrdinalIgnoreCase))
        { return TooLarge(); }
        catch (InvalidDataException)
        { return Results.BadRequest(new { codigo = "UPLOAD_INVALIDO", mensagem = "O formulário de upload é inválido ou excede o limite permitido." }); }

        if (form.Files.Count != 1 || form.Files[0].Name != "arquivo")
            return Results.BadRequest(new { codigo = "ARQUIVO_OBRIGATORIO", mensagem = "Envie exatamente um arquivo no campo arquivo." });

        var upload = form.Files[0];
        if (upload.Length > ImportContract.MaxFileBytes) return TooLarge();
        var name = Path.GetFileName(upload.FileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\0'))
            return Results.BadRequest(new { codigo = "NOME_INVALIDO", mensagem = "Informe um nome de arquivo válido." });

        await using var input = upload.OpenReadStream();
        using var content = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (content.Length + count > ImportContract.MaxFileBytes) return TooLarge();
            await content.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }

        var result = await service.ImportAsync(source, name, content.ToArray(), cancellationToken);
        if (result.ImportacaoExistenteId is { } existingId)
            return Results.Conflict(new { codigo = "ARQUIVO_JA_IMPORTADO",
                mensagem = "Este conteúdo já foi importado para essa fonte.", importacaoExistenteId = existingId });
        if (result.Erros.Count > 0)
            return Results.UnprocessableEntity(new { codigo = "CSV_REJEITADO", erros = result.Erros });
        return Results.Created($"/api/importacoes/{result.Importacao!.Id}", result.Importacao);
    }

    private static IResult TooLarge() => Results.Problem(statusCode: 413,
        title: "Arquivo muito grande", detail: "O arquivo deve ter no máximo 10 MB (10.485.760 bytes).");
}
