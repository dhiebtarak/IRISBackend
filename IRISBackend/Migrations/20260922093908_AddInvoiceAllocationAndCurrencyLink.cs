using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceAllocationAndCurrencyLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Id_currency",
                table: "INVOICE",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Id_receipt_allocation",
                table: "INVOICE",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_INVOICE_Id_currency",
                table: "INVOICE",
                column: "Id_currency");

            migrationBuilder.CreateIndex(
                name: "IX_INVOICE_id_receipt_allocation",
                table: "INVOICE",
                column: "Id_receipt_allocation");

            migrationBuilder.AddForeignKey(
                name: "FK_INVOICE_id_currency",
                table: "INVOICE",
                column: "Id_currency",
                principalTable: "CURRENCY",
                principalColumn: "Id_currency");

            migrationBuilder.AddForeignKey(
                name: "FK_INVOICE_id_receipt_allocation",
                table: "INVOICE",
                column: "Id_receipt_allocation",
                principalTable: "RECEIPT_ALLOCATION",
                principalColumn: "Id_receipt_allocation");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_INVOICE_id_currency",
                table: "INVOICE");

            migrationBuilder.DropForeignKey(
                name: "FK_INVOICE_id_receipt_allocation",
                table: "INVOICE");

            migrationBuilder.DropIndex(
                name: "IX_INVOICE_Id_currency",
                table: "INVOICE");

            migrationBuilder.DropIndex(
                name: "IX_INVOICE_id_receipt_allocation",
                table: "INVOICE");

            migrationBuilder.DropColumn(
                name: "Id_currency",
                table: "INVOICE");

            migrationBuilder.DropColumn(
                name: "Id_receipt_allocation",
                table: "INVOICE");
        }
    }
}
