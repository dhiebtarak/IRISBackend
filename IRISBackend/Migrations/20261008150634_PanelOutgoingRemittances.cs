using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations
{
    /// <inheritdoc />
    public partial class PanelOutgoingRemittances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "id_invoice_allocation",
                table: "REMITTANCE_REF",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "REMITTANCE_REF",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "RECEIPT_ALLOCATION",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "OUTGOING_CLOSING",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Reference = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    IdCounterparty = table.Column<int>(type: "int", nullable: false),
                    IdCurrency = table.Column<int>(type: "int", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    TotalInWords = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    PdfContent = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OUTGOING_CLOSING", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OUTGOING_CLOSING_AspNetUsers_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OUTGOING_CLOSING_CURRENCY_IdCurrency",
                        column: x => x.IdCurrency,
                        principalTable: "CURRENCY",
                        principalColumn: "Id_currency");
                    table.ForeignKey(
                        name: "FK_OUTGOING_CLOSING_Counterparties_IdCounterparty",
                        column: x => x.IdCounterparty,
                        principalTable: "Counterparties",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OUTGOING_CLOSING_LINE",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClosingId = table.Column<int>(type: "int", nullable: false),
                    RemittanceId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    PayableAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OUTGOING_CLOSING_LINE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OUTGOING_CLOSING_LINE_OUTGOING_CLOSING_ClosingId",
                        column: x => x.ClosingId,
                        principalTable: "OUTGOING_CLOSING",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OUTGOING_CLOSING_LINE_REMITTANCE_REF_RemittanceId",
                        column: x => x.RemittanceId,
                        principalTable: "REMITTANCE_REF",
                        principalColumn: "Remittance_ref");
                });

            migrationBuilder.CreateIndex(
                name: "IX_REMITTANCE_REF_id_invoice_allocation",
                table: "REMITTANCE_REF",
                column: "id_invoice_allocation");

            migrationBuilder.CreateIndex(
                name: "IX_OUTGOING_CLOSING_CreatedBy",
                table: "OUTGOING_CLOSING",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_OUTGOING_CLOSING_IdCounterparty",
                table: "OUTGOING_CLOSING",
                column: "IdCounterparty");

            migrationBuilder.CreateIndex(
                name: "IX_OUTGOING_CLOSING_IdCurrency",
                table: "OUTGOING_CLOSING",
                column: "IdCurrency");

            migrationBuilder.CreateIndex(
                name: "IX_OUTGOING_CLOSING_Reference",
                table: "OUTGOING_CLOSING",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OUTGOING_CLOSING_LINE_ClosingId_RemittanceId",
                table: "OUTGOING_CLOSING_LINE",
                columns: new[] { "ClosingId", "RemittanceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OUTGOING_CLOSING_LINE_RemittanceId",
                table: "OUTGOING_CLOSING_LINE",
                column: "RemittanceId",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_REMITTANCE_REF_INVOICE_PANEL_ALLOCATION_id_invoice_allocation",
                table: "REMITTANCE_REF",
                column: "id_invoice_allocation",
                principalTable: "INVOICE_PANEL_ALLOCATION",
                principalColumn: "id_invoice_allocation");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_REMITTANCE_REF_INVOICE_PANEL_ALLOCATION_id_invoice_allocation",
                table: "REMITTANCE_REF");

            migrationBuilder.DropTable(
                name: "OUTGOING_CLOSING_LINE");

            migrationBuilder.DropTable(
                name: "OUTGOING_CLOSING");

            migrationBuilder.DropIndex(
                name: "IX_REMITTANCE_REF_id_invoice_allocation",
                table: "REMITTANCE_REF");

            migrationBuilder.DropColumn(
                name: "id_invoice_allocation",
                table: "REMITTANCE_REF");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "REMITTANCE_REF");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "RECEIPT_ALLOCATION");
        }
    }
}
