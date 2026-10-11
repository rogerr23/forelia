using Microsoft.EntityFrameworkCore;

namespace Forelia.Api.Data;

public sealed class ForeliaDbContext(DbContextOptions<ForeliaDbContext> options)
    : DbContext(options)
{
    public DbSet<ImportedFile> ImportedFiles => Set<ImportedFile>();
    public DbSet<FinancialRecord> FinancialRecords => Set<FinancialRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var file = modelBuilder.Entity<ImportedFile>();
        file.ToTable("arquivo_importado", table =>
        {
            table.HasCheckConstraint("ck_arquivo_fonte", "fonte IN ('OPERACIONAL', 'CONTABIL')");
            table.HasCheckConstraint("ck_arquivo_tamanho", "tamanho_bytes > 0 AND tamanho_bytes <= 10485760 AND octet_length(conteudo_original) = tamanho_bytes");
            table.HasCheckConstraint("ck_arquivo_total", "total_registros > 0");
        });
        file.HasKey(x => x.Id);
        file.Property(x => x.Id).HasColumnName("id");
        file.Property(x => x.Source).HasColumnName("fonte").HasConversion<string>();
        file.Property(x => x.OriginalName).HasColumnName("nome_original").IsRequired();
        file.Property(x => x.ContentHash).HasColumnName("hash_conteudo").HasMaxLength(64).IsRequired();
        file.Property(x => x.OriginalContent).HasColumnName("conteudo_original").HasColumnType("bytea").IsRequired();
        file.Property(x => x.SizeBytes).HasColumnName("tamanho_bytes");
        file.Property(x => x.RecordCount).HasColumnName("total_registros");
        file.Property(x => x.ImportedAt).HasColumnName("importado_em");
        file.Property(x => x.LayoutVersion).HasColumnName("versao_layout").IsRequired();
        file.Property(x => x.ImporterVersion).HasColumnName("versao_importador").IsRequired();
        file.HasIndex(x => new { x.Source, x.ContentHash }).IsUnique().HasDatabaseName("ux_arquivo_fonte_hash");

        var record = modelBuilder.Entity<FinancialRecord>();
        record.ToTable("registro_financeiro", table =>
        {
            table.HasCheckConstraint("ck_registro_linha", "numero_linha > 1");
            table.HasCheckConstraint("ck_registro_identificador", "length(btrim(identificador_externo)) > 0");
            table.HasCheckConstraint("ck_registro_valor", "valor = trunc(valor, 2) AND valor BETWEEN -79228162514264337593543950335 AND 79228162514264337593543950335");
        });
        record.HasKey(x => x.Id);
        record.Property(x => x.Id).HasColumnName("id");
        record.Property(x => x.FileId).HasColumnName("arquivo_id");
        record.Property(x => x.LineNumber).HasColumnName("numero_linha");
        record.Property(x => x.ExternalId).HasColumnName("identificador_externo").IsRequired();
        record.Property(x => x.OperationDate).HasColumnName("data_operacao");
        // numeric sem escala fixa evita arredondamento silencioso no PostgreSQL.
        record.Property(x => x.Amount).HasColumnName("valor").HasColumnType("numeric");
        record.HasIndex(x => new { x.FileId, x.LineNumber }).IsUnique();
        file.HasMany(x => x.Records).WithOne().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Restrict);
    }
}
