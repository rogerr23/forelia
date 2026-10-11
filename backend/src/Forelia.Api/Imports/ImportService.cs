using System.Security.Cryptography;
using Forelia.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Forelia.Api.Imports;

public sealed class ImportService(ForeliaDbContext database, CsvValidator validator)
{
    public async Task<ImportResult> ImportAsync(SourceType source, string originalName, byte[] content,
        CancellationToken cancellationToken = default)
    {
        var validation = validator.Validate(content, cancellationToken);
        if (!validation.IsValid) return new(null, validation.Erros);

        var hash = Convert.ToHexStringLower(SHA256.HashData(content));
        var existingId = await FindExistingAsync(source, hash, cancellationToken);
        if (existingId is not null) return new(null, [], existingId);

        var file = new ImportedFile
        {
            Source = source,
            OriginalName = originalName,
            ContentHash = hash,
            OriginalContent = content,
            SizeBytes = content.Length,
            RecordCount = validation.Registros.Count,
            Records = validation.Registros.Select(row => new FinancialRecord
            {
                LineNumber = row.Linha,
                ExternalId = row.IdOperacao,
                OperationDate = row.DataOperacao,
                Amount = row.Valor
            }).ToList()
        };

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            database.ImportedFiles.Add(file);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(ToResponse(file), []);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
               { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_arquivo_fonte_hash" })
        {
            await transaction.RollbackAsync(cancellationToken);
            database.ChangeTracker.Clear();
            existingId = await FindExistingAsync(source, hash, cancellationToken);
            if (existingId is null) throw;
            return new(null, [], existingId);
        }
    }

    private Task<Guid?> FindExistingAsync(SourceType source, string hash, CancellationToken cancellationToken) =>
        database.ImportedFiles.AsNoTracking().Where(x => x.Source == source && x.ContentHash == hash)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken);

    public static ImportResponse ToResponse(ImportedFile file) => new(file.Id, file.Source.ToString(),
        file.OriginalName, file.ContentHash, file.SizeBytes, file.RecordCount, file.ImportedAt, file.LayoutVersion);
}
