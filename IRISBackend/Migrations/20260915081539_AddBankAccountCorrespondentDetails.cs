using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddBankAccountCorrespondentDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Bank_city",
                table: "COUNTERPARTY_BANK_ACCOUNT",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Bank_country",
                table: "COUNTERPARTY_BANK_ACCOUNT",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Correspondent_bank_name",
                table: "COUNTERPARTY_BANK_ACCOUNT",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Correspondent_swift_bic",
                table: "COUNTERPARTY_BANK_ACCOUNT",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Has_correspondent_bank",
                table: "COUNTERPARTY_BANK_ACCOUNT",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Bank_city",
                table: "COUNTERPARTY_BANK_ACCOUNT");

            migrationBuilder.DropColumn(
                name: "Bank_country",
                table: "COUNTERPARTY_BANK_ACCOUNT");

            migrationBuilder.DropColumn(
                name: "Correspondent_bank_name",
                table: "COUNTERPARTY_BANK_ACCOUNT");

            migrationBuilder.DropColumn(
                name: "Correspondent_swift_bic",
                table: "COUNTERPARTY_BANK_ACCOUNT");

            migrationBuilder.DropColumn(
                name: "Has_correspondent_bank",
                table: "COUNTERPARTY_BANK_ACCOUNT");
        }
    }
}
