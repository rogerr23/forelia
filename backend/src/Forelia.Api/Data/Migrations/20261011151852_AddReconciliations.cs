using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forelia.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReconciliations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conciliacao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationalFileId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountingFileId = table.Column<Guid>(type: "uuid", nullable: false),
                    RulesVersion = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorCode = table.Column<string>(type: "text", nullable: true),
                    SourceRecords = table.Column<int>(type: "integer", nullable: false),
                    MatchedPairs = table.Column<int>(type: "integer", nullable: false),
                    DivergentPairs = table.Column<int>(type: "integer", nullable: false),
                    MissingAccounting = table.Column<int>(type: "integer", nullable: false),
                    MissingOperational = table.Column<int>(type: "integer", nullable: false),
                    ReconciliationRate = table.Column<decimal>(type: "numeric(5,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conciliacao", x => x.Id);
                    table.CheckConstraint("ck_conciliacao_arquivos", "\"OperationalFileId\" <> \"AccountingFileId\"");
                    table.CheckConstraint("ck_conciliacao_status", "\"Status\" IN ('PENDENTE', 'PROCESSANDO', 'CONCLUIDA', 'FALHOU')");
                    table.ForeignKey(
                        name: "FK_conciliacao_arquivo_importado_AccountingFileId",
                        column: x => x.AccountingFileId,
                        principalTable: "arquivo_importado",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_conciliacao_arquivo_importado_OperationalFileId",
                        column: x => x.OperationalFileId,
                        principalTable: "arquivo_importado",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "item_conciliacao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReconciliationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "text", nullable: false),
                    OperationalRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    AccountingRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    OperationalAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    AccountingAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    OperationalDate = table.Column<DateOnly>(type: "date", nullable: true),
                    AccountingDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Result = table.Column<string>(type: "text", nullable: false),
                    ValueDivergence = table.Column<bool>(type: "boolean", nullable: false),
                    DateDivergence = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_conciliacao", x => x.Id);
                    table.CheckConstraint("ck_item_resultado", "\"Result\" IN ('CONCILIADO', 'DIVERGENTE', 'SEM_REGISTRO_CONTABIL', 'SEM_OPERACAO_FINANCEIRA')");
                    table.ForeignKey(
                        name: "FK_item_conciliacao_conciliacao_ReconciliationId",
                        column: x => x.ReconciliationId,
                        principalTable: "conciliacao",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_item_conciliacao_registro_financeiro_AccountingRecordId",
                        column: x => x.AccountingRecordId,
                        principalTable: "registro_financeiro",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_item_conciliacao_registro_financeiro_OperationalRecordId",
                        column: x => x.OperationalRecordId,
                        principalTable: "registro_financeiro",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_conciliacao_AccountingFileId",
                table: "conciliacao",
                column: "AccountingFileId");

            migrationBuilder.CreateIndex(
                name: "ux_conciliacao_concluida",
                table: "conciliacao",
                columns: new[] { "OperationalFileId", "AccountingFileId", "RulesVersion" },
                unique: true,
                filter: "\"Status\" = 'CONCLUIDA'");

            migrationBuilder.CreateIndex(
                name: "IX_item_conciliacao_AccountingRecordId",
                table: "item_conciliacao",
                column: "AccountingRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_item_conciliacao_OperationalRecordId",
                table: "item_conciliacao",
                column: "OperationalRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_item_conciliacao_ReconciliationId_ExternalId",
                table: "item_conciliacao",
                columns: new[] { "ReconciliationId", "ExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "item_conciliacao");

            migrationBuilder.DropTable(
                name: "conciliacao");
        }
    }
}
