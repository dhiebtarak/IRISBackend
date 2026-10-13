using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations
{
    /// <inheritdoc />
    public partial class FixInvoicePairingIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_RECEIPT_ALLOCATION_receipt_invoice",
                table: "RECEIPT_ALLOCATION");

            migrationBuilder.CreateTable(
                name: "INVOICE_PAIRING_OPERATION",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReceiptId = table.Column<int>(type: "int", nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    TotalAllocated = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    RemainingAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    PairingStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_INVOICE_PAIRING_OPERATION", x => x.Id);
                    table.ForeignKey(
                        name: "FK_INVOICE_PAIRING_OPERATION_AspNetUsers_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_INVOICE_PAIRING_OPERATION_BANK_RECEIPT_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "BANK_RECEIPT",
                        principalColumn: "Id_bank_receipt");
                });

            migrationBuilder.CreateTable(
                name: "INVOICE_PAIRING_OPERATION_LINE",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OperationId = table.Column<int>(type: "int", nullable: false),
                    ReceiptAllocationId = table.Column<int>(type: "int", nullable: false),
                    RequestedAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_INVOICE_PAIRING_OPERATION_LINE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_INVOICE_PAIRING_OPERATION_LINE_INVOICE_PAIRING_OPERATION_OperationId",
                        column: x => x.OperationId,
                        principalTable: "INVOICE_PAIRING_OPERATION",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_INVOICE_PAIRING_OPERATION_LINE_RECEIPT_ALLOCATION_ReceiptAllocationId",
                        column: x => x.ReceiptAllocationId,
                        principalTable: "RECEIPT_ALLOCATION",
                        principalColumn: "Id_receipt_allocation");
                });

            migrationBuilder.CreateIndex(
                name: "UX_RECEIPT_ALLOCATION_receipt_invoice",
                table: "RECEIPT_ALLOCATION",
                columns: new[] { "Id_bank_receipt", "id_invoice" },
                unique: true,
                filter: "[id_invoice] IS NOT NULL AND [is_active] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_INVOICE_PAIRING_OPERATION_CreatedBy",
                table: "INVOICE_PAIRING_OPERATION",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_INVOICE_PAIRING_OPERATION_ReceiptId_RequestHash",
                table: "INVOICE_PAIRING_OPERATION",
                columns: new[] { "ReceiptId", "RequestHash" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_INVOICE_PAIRING_OPERATION_LINE_OperationId_ReceiptAllocationId",
                table: "INVOICE_PAIRING_OPERATION_LINE",
                columns: new[] { "OperationId", "ReceiptAllocationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_INVOICE_PAIRING_OPERATION_LINE_ReceiptAllocationId",
                table: "INVOICE_PAIRING_OPERATION_LINE",
                column: "ReceiptAllocationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "INVOICE_PAIRING_OPERATION_LINE");

            migrationBuilder.DropTable(
                name: "INVOICE_PAIRING_OPERATION");

            migrationBuilder.DropIndex(
                name: "UX_RECEIPT_ALLOCATION_receipt_invoice",
                table: "RECEIPT_ALLOCATION");

            migrationBuilder.CreateIndex(
                name: "UX_RECEIPT_ALLOCATION_receipt_invoice",
                table: "RECEIPT_ALLOCATION",
                columns: new[] { "Id_bank_receipt", "id_invoice" },
                unique: true,
                filter: "[id_invoice] IS NOT NULL");
        }
    }
}
