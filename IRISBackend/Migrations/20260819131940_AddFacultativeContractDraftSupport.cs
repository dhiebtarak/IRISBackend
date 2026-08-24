using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddFacultativeContractDraftSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Umr",
                table: "CONTRACT",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Cedent_premium_reference",
                table: "CONTRACT",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_CONTRACT_UMR",
                table: "CONTRACT",
                column: "Umr",
                unique: true,
                filter: "[Umr] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_CONTRACT_UMR",
                table: "CONTRACT");

            migrationBuilder.DropColumn(
                name: "Cedent_premium_reference",
                table: "CONTRACT");

            migrationBuilder.AlterColumn<int>(
                name: "Umr",
                table: "CONTRACT",
                type: "int",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);
        }
    }
}
