using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forelia.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalystObservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AnalysisStatus",
                table: "item_conciliacao",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "observacao_analista",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReconciliationItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Analyst = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_observacao_analista", x => x.Id);
                    table.CheckConstraint("ck_observacao_analista", "length(btrim(\"Analyst\")) > 0");
                    table.CheckConstraint("ck_observacao_texto", "length(btrim(\"Text\")) > 0");
                    table.ForeignKey(
                        name: "FK_observacao_analista_item_conciliacao_ReconciliationItemId",
                        column: x => x.ReconciliationItemId,
                        principalTable: "item_conciliacao",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("""
                UPDATE item_conciliacao SET "AnalysisStatus" = 'PENDENTE'
                WHERE "Result" <> 'CONCILIADO';
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_item_situacao_analise",
                table: "item_conciliacao",
                sql: "(\"Result\" = 'CONCILIADO' AND \"AnalysisStatus\" IS NULL) OR (\"Result\" <> 'CONCILIADO' AND \"AnalysisStatus\" IS NOT NULL AND \"AnalysisStatus\" IN ('PENDENTE', 'EM_ANALISE', 'ANALISADA'))");

            migrationBuilder.CreateIndex(
                name: "IX_observacao_analista_ReconciliationItemId_CreatedAt_Id",
                table: "observacao_analista",
                columns: new[] { "ReconciliationItemId", "CreatedAt", "Id" });

            migrationBuilder.Sql("""
                CREATE FUNCTION forelia_observacao_imutavel() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Observações não podem ser editadas ou excluídas.';
                END; $$;
                CREATE TRIGGER observacao_imutavel BEFORE UPDATE OR DELETE ON observacao_analista
                FOR EACH ROW EXECUTE FUNCTION forelia_observacao_imutavel();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "observacao_analista");

            migrationBuilder.Sql("DROP FUNCTION forelia_observacao_imutavel();");

            migrationBuilder.DropCheckConstraint(
                name: "ck_item_situacao_analise",
                table: "item_conciliacao");

            migrationBuilder.DropColumn(
                name: "AnalysisStatus",
                table: "item_conciliacao");
        }
    }
}
