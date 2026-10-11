using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forelia.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "arquivo_importado",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fonte = table.Column<string>(type: "text", nullable: false),
                    nome_original = table.Column<string>(type: "text", nullable: false),
                    hash_conteudo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    conteudo_original = table.Column<byte[]>(type: "bytea", nullable: false),
                    tamanho_bytes = table.Column<long>(type: "bigint", nullable: false),
                    total_registros = table.Column<int>(type: "integer", nullable: false),
                    importado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    versao_layout = table.Column<string>(type: "text", nullable: false),
                    versao_importador = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_arquivo_importado", x => x.id);
                    table.CheckConstraint("ck_arquivo_fonte", "fonte IN ('OPERACIONAL', 'CONTABIL')");
                    table.CheckConstraint("ck_arquivo_tamanho", "tamanho_bytes > 0 AND tamanho_bytes <= 10485760 AND octet_length(conteudo_original) = tamanho_bytes");
                    table.CheckConstraint("ck_arquivo_total", "total_registros > 0");
                });

            migrationBuilder.CreateTable(
                name: "registro_financeiro",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    arquivo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_linha = table.Column<int>(type: "integer", nullable: false),
                    identificador_externo = table.Column<string>(type: "text", nullable: false),
                    data_operacao = table.Column<DateOnly>(type: "date", nullable: false),
                    valor = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_registro_financeiro", x => x.id);
                    table.CheckConstraint("ck_registro_identificador", "length(btrim(identificador_externo)) > 0");
                    table.CheckConstraint("ck_registro_linha", "numero_linha > 1");
                    table.CheckConstraint("ck_registro_valor", "valor = trunc(valor, 2) AND valor BETWEEN -79228162514264337593543950335 AND 79228162514264337593543950335");
                    table.ForeignKey(
                        name: "FK_registro_financeiro_arquivo_importado_arquivo_id",
                        column: x => x.arquivo_id,
                        principalTable: "arquivo_importado",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_arquivo_fonte_hash",
                table: "arquivo_importado",
                columns: new[] { "fonte", "hash_conteudo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_registro_financeiro_arquivo_id_numero_linha",
                table: "registro_financeiro",
                columns: new[] { "arquivo_id", "numero_linha" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "registro_financeiro");

            migrationBuilder.DropTable(
                name: "arquivo_importado");
        }
    }
}
