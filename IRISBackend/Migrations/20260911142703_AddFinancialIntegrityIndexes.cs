using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialIntegrityIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ;WITH Defaults AS
                (
                    SELECT Id_bank_account,
                           ROW_NUMBER() OVER (PARTITION BY Id_counterparty, Id_currency ORDER BY Id_bank_account) AS row_number
                    FROM COUNTERPARTY_BANK_ACCOUNT
                    WHERE Is_active = 1 AND Is_default = 1
                )
                UPDATE account
                SET Is_default = 0
                FROM COUNTERPARTY_BANK_ACCOUNT account
                INNER JOIN Defaults duplicate_default ON duplicate_default.Id_bank_account = account.Id_bank_account
                WHERE duplicate_default.row_number > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "UX_REMITTANCE_REF_allocation_panel",
                table: "REMITTANCE_REF",
                columns: new[] { "Id_receipt_allocation", "Id_panel" },
                unique: true,
                filter: "[Id_receipt_allocation] IS NOT NULL AND [Id_panel] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_COUNTERPARTY_BANK_ACCOUNT_default_currency",
                table: "COUNTERPARTY_BANK_ACCOUNT",
                columns: new[] { "Id_counterparty", "Id_currency" },
                unique: true,
                filter: "[Is_active]=(1) AND [Is_default]=(1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_REMITTANCE_REF_allocation_panel",
                table: "REMITTANCE_REF");

            migrationBuilder.DropIndex(
                name: "UX_COUNTERPARTY_BANK_ACCOUNT_default_currency",
                table: "COUNTERPARTY_BANK_ACCOUNT");

            migrationBuilder.CreateIndex(
                name: "IX_REMITTANCE_REF_Id_receipt_allocation",
                table: "REMITTANCE_REF",
                column: "Id_receipt_allocation");

            migrationBuilder.CreateIndex(
                name: "IX_COUNTERPARTY_BANK_ACCOUNT_Id_counterparty",
                table: "COUNTERPARTY_BANK_ACCOUNT",
                column: "Id_counterparty");
        }
    }
}
