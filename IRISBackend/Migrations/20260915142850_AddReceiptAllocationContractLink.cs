using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptAllocationContractLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Id_contract",
                table: "RECEIPT_ALLOCATION",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RECEIPT_ALLOCATION_Id_contract",
                table: "RECEIPT_ALLOCATION",
                column: "Id_contract");

            migrationBuilder.AddForeignKey(
                name: "FK_RECEIPT_ALLOCATION_id_contract",
                table: "RECEIPT_ALLOCATION",
                column: "Id_contract",
                principalTable: "CONTRACT",
                principalColumn: "Id_contract");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RECEIPT_ALLOCATION_id_contract",
                table: "RECEIPT_ALLOCATION");

            migrationBuilder.DropIndex(
                name: "IX_RECEIPT_ALLOCATION_Id_contract",
                table: "RECEIPT_ALLOCATION");

            migrationBuilder.DropColumn(
                name: "Id_contract",
                table: "RECEIPT_ALLOCATION");
        }
    }
}
