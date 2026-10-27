using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddAvoirSettlementAndRefundWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "beneficiary_type",
                table: "REMITTANCE_REF",
                type: "nvarchar(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source_invoice_id",
                table: "REMITTANCE_REF",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_REMITTANCE_REF_source_invoice_id",
                table: "REMITTANCE_REF",
                column: "source_invoice_id");

            migrationBuilder.CreateTable(
                name: "AVOIR_REFUND_SETTLEMENT",
                columns: table => new
                {
                    id_avoir_refund_settlement = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    avoir_invoice_id = table.Column<int>(type: "int", nullable: false),
                    id_bank_receipt = table.Column<int>(type: "int", nullable: false),
                    id_remittance_ref = table.Column<int>(type: "int", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AVOIR_REFUND_SETTLEMENT", x => x.id_avoir_refund_settlement);
                    table.CheckConstraint("CK_AVOIR_REFUND_SETTLEMENT_amount_positive", "[amount] > (0)");
                    table.ForeignKey(
                        name: "FK_AVOIR_REFUND_SETTLEMENT_avoir_invoice",
                        column: x => x.avoir_invoice_id,
                        principalTable: "INVOICE",
                        principalColumn: "Id_invoice",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AVOIR_REFUND_SETTLEMENT_bank_receipt",
                        column: x => x.id_bank_receipt,
                        principalTable: "BANK_RECEIPT",
                        principalColumn: "Id_bank_receipt",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AVOIR_REFUND_SETTLEMENT_created_by",
                        column: x => x.created_by,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AVOIR_REFUND_SETTLEMENT_remittance",
                        column: x => x.id_remittance_ref,
                        principalTable: "REMITTANCE_REF",
                        principalColumn: "Remittance_ref",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AVOIR_REFUND_SETTLEMENT_avoir_active",
                table: "AVOIR_REFUND_SETTLEMENT",
                columns: new[] { "avoir_invoice_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_AVOIR_REFUND_SETTLEMENT_created_by",
                table: "AVOIR_REFUND_SETTLEMENT",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_AVOIR_REFUND_SETTLEMENT_id_bank_receipt",
                table: "AVOIR_REFUND_SETTLEMENT",
                column: "id_bank_receipt");

            migrationBuilder.CreateIndex(
                name: "IX_AVOIR_REFUND_SETTLEMENT_id_remittance_ref",
                table: "AVOIR_REFUND_SETTLEMENT",
                column: "id_remittance_ref");

            migrationBuilder.CreateIndex(
                name: "UX_AVOIR_REFUND_SETTLEMENT_idempotency_key",
                table: "AVOIR_REFUND_SETTLEMENT",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_REMITTANCE_REF_source_invoice",
                table: "REMITTANCE_REF",
                column: "source_invoice_id",
                principalTable: "INVOICE",
                principalColumn: "Id_invoice",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_REMITTANCE_REF_source_invoice",
                table: "REMITTANCE_REF");

            migrationBuilder.DropIndex(
                name: "IX_REMITTANCE_REF_source_invoice_id",
                table: "REMITTANCE_REF");

            migrationBuilder.DropTable(
                name: "AVOIR_REFUND_SETTLEMENT");

            migrationBuilder.DropColumn(
                name: "beneficiary_type",
                table: "REMITTANCE_REF");

            migrationBuilder.DropColumn(
                name: "source_invoice_id",
                table: "REMITTANCE_REF");

        }
    }
}
