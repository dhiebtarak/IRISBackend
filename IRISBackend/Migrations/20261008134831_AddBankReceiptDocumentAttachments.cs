using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddBankReceiptDocumentAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "id_bank_receipt",
                table: "DOCUMENT_ATTACHMENT",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DOCUMENT_ATTACHMENT_id_bank_receipt",
                table: "DOCUMENT_ATTACHMENT",
                column: "id_bank_receipt");

            migrationBuilder.AddForeignKey(
                name: "FK_DOCUMENT_ATTACHMENT_id_bank_receipt",
                table: "DOCUMENT_ATTACHMENT",
                column: "id_bank_receipt",
                principalTable: "BANK_RECEIPT",
                principalColumn: "Id_bank_receipt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DOCUMENT_ATTACHMENT_id_bank_receipt",
                table: "DOCUMENT_ATTACHMENT");

            migrationBuilder.DropIndex(
                name: "IX_DOCUMENT_ATTACHMENT_id_bank_receipt",
                table: "DOCUMENT_ATTACHMENT");

            migrationBuilder.DropColumn(
                name: "id_bank_receipt",
                table: "DOCUMENT_ATTACHMENT");
        }
    }
}
