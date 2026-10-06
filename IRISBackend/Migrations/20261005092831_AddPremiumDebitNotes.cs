using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddPremiumDebitNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Gross_premium",
                table: "INVOICE",
                type: "decimal(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Idempotency_key",
                table: "INVOICE",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Sequence_number",
                table: "INVOICE",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "INVOICE_PANEL_ALLOCATION",
                columns: table => new
                {
                    id_invoice_allocation = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    id_invoice = table.Column<int>(type: "int", nullable: false),
                    id_panel = table.Column<int>(type: "int", nullable: false),
                    id_counterparty = table.Column<int>(type: "int", nullable: false),
                    reinsurer_name_snapshot = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    signed_share_pct_snapshot = table.Column<decimal>(type: "decimal(19,8)", nullable: true),
                    allocation_ratio = table.Column<decimal>(type: "decimal(19,8)", nullable: false),
                    gross_premium = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    cedent_commission_pct = table.Column<decimal>(type: "decimal(19,8)", nullable: true),
                    cedent_commission_amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    brokerage_pct = table.Column<decimal>(type: "decimal(19,8)", nullable: true),
                    brokerage_amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    other_deductions = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    net_amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    currency_code_snapshot = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_INVOICE_PANEL_ALLOCATION", x => x.id_invoice_allocation);
                    table.ForeignKey(
                        name: "FK_INVOICE_PANEL_ALLOCATION_CONTRACT_PANEL_id_panel",
                        column: x => x.id_panel,
                        principalTable: "CONTRACT_PANEL",
                        principalColumn: "Id_panel");
                    table.ForeignKey(
                        name: "FK_INVOICE_PANEL_ALLOCATION_Counterparties_id_counterparty",
                        column: x => x.id_counterparty,
                        principalTable: "Counterparties",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_INVOICE_PANEL_ALLOCATION_INVOICE_id_invoice",
                        column: x => x.id_invoice,
                        principalTable: "INVOICE",
                        principalColumn: "Id_invoice",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_INVOICE_idempotency_key",
                table: "INVOICE",
                column: "Idempotency_key",
                unique: true,
                filter: "[Idempotency_key] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_INVOICE_PANEL_ALLOCATION_id_counterparty",
                table: "INVOICE_PANEL_ALLOCATION",
                column: "id_counterparty");

            migrationBuilder.CreateIndex(
                name: "IX_INVOICE_PANEL_ALLOCATION_id_panel",
                table: "INVOICE_PANEL_ALLOCATION",
                column: "id_panel");

            migrationBuilder.CreateIndex(
                name: "UX_INVOICE_PANEL_ALLOCATION_invoice_panel",
                table: "INVOICE_PANEL_ALLOCATION",
                columns: new[] { "id_invoice", "id_panel" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "INVOICE_PANEL_ALLOCATION");

            migrationBuilder.DropIndex(
                name: "UX_INVOICE_idempotency_key",
                table: "INVOICE");

            migrationBuilder.DropColumn(
                name: "Gross_premium",
                table: "INVOICE");

            migrationBuilder.DropColumn(
                name: "Idempotency_key",
                table: "INVOICE");

            migrationBuilder.DropColumn(
                name: "Sequence_number",
                table: "INVOICE");
        }
    }
}
