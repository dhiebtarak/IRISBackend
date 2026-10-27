using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddIrisBankAccountDocumentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "account_holder",
                table: "IRIS_BANK_ACCOUNT",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "domiciliation",
                table: "IRIS_BANK_ACCOUNT",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rib",
                table: "IRIS_BANK_ACCOUNT",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "account_holder",
                table: "IRIS_BANK_ACCOUNT");

            migrationBuilder.DropColumn(
                name: "domiciliation",
                table: "IRIS_BANK_ACCOUNT");

            migrationBuilder.DropColumn(
                name: "rib",
                table: "IRIS_BANK_ACCOUNT");
        }
    }
}
