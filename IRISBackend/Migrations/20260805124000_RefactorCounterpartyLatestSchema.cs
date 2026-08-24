using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations
{
    /// <inheritdoc />
    public partial class RefactorCounterpartyLatestSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Counterparties_BusinessIdentifier",
                table: "Counterparties");

            migrationBuilder.DropColumn(
                name: "BusinessIdentifier",
                table: "Counterparties");

            migrationBuilder.AddColumn<string>(
                name: "Activity",
                table: "Counterparties",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CedantLegalEntity",
                table: "Counterparties",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CommercialRegister",
                table: "Counterparties",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactName",
                table: "Counterparties",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LegacyClientId",
                table: "Counterparties",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegacyCompanyCode",
                table: "Counterparties",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Observations",
                table: "Counterparties",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "Counterparties",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Counterparties",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.AddColumn<string>(
                name: "Website",
                table: "Counterparties",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Counterparties_LegacyClientId",
                table: "Counterparties",
                column: "LegacyClientId",
                unique: true,
                filter: "[LegacyClientId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Counterparties_LegacyCompanyCode",
                table: "Counterparties",
                column: "LegacyCompanyCode",
                unique: true,
                filter: "[LegacyCompanyCode] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Counterparties_LegacyClientId",
                table: "Counterparties");

            migrationBuilder.DropIndex(
                name: "IX_Counterparties_LegacyCompanyCode",
                table: "Counterparties");

            migrationBuilder.DropColumn(
                name: "Activity",
                table: "Counterparties");

            migrationBuilder.DropColumn(
                name: "CedantLegalEntity",
                table: "Counterparties");

            migrationBuilder.DropColumn(
                name: "CommercialRegister",
                table: "Counterparties");

            migrationBuilder.DropColumn(
                name: "ContactName",
                table: "Counterparties");

            migrationBuilder.DropColumn(
                name: "LegacyClientId",
                table: "Counterparties");

            migrationBuilder.DropColumn(
                name: "LegacyCompanyCode",
                table: "Counterparties");

            migrationBuilder.DropColumn(
                name: "Observations",
                table: "Counterparties");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                table: "Counterparties");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Counterparties");

            migrationBuilder.DropColumn(
                name: "Website",
                table: "Counterparties");

            migrationBuilder.AddColumn<string>(
                name: "BusinessIdentifier",
                table: "Counterparties",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: string.Empty);

            migrationBuilder.CreateIndex(
                name: "IX_Counterparties_BusinessIdentifier",
                table: "Counterparties",
                column: "BusinessIdentifier",
                unique: true);
        }
    }
}
