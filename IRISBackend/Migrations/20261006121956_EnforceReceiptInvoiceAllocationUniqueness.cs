using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations
{
    /// <inheritdoc />
    public partial class EnforceReceiptInvoiceAllocationUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RECEIPT_ALLOCATION_Id_bank_receipt' AND object_id = OBJECT_ID(N'dbo.RECEIPT_ALLOCATION')) DROP INDEX [IX_RECEIPT_ALLOCATION_Id_bank_receipt] ON [dbo].[RECEIPT_ALLOCATION];");

            migrationBuilder.CreateIndex(
                name: "UX_RECEIPT_ALLOCATION_receipt_invoice",
                table: "RECEIPT_ALLOCATION",
                columns: new[] { "Id_bank_receipt", "id_invoice" },
                unique: true,
                filter: "[id_invoice] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_RECEIPT_ALLOCATION_receipt_invoice",
                table: "RECEIPT_ALLOCATION");

            migrationBuilder.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RECEIPT_ALLOCATION_Id_bank_receipt' AND object_id = OBJECT_ID(N'dbo.RECEIPT_ALLOCATION')) CREATE INDEX [IX_RECEIPT_ALLOCATION_Id_bank_receipt] ON [dbo].[RECEIPT_ALLOCATION] ([Id_bank_receipt]);");
        }
    }
}
