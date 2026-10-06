using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddDisbursementPaymentSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Bank_city_snapshot",
                table: "DISBURSEMENT",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Bank_country_snapshot",
                table: "DISBURSEMENT",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Bank_name_snapshot",
                table: "DISBURSEMENT",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Cedent_name_snapshot",
                table: "DISBURSEMENT",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "Closed_at",
                table: "DISBURSEMENT",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Closed_by",
                table: "DISBURSEMENT",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Contract_name_snapshot",
                table: "DISBURSEMENT",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Correspondent_bank_name_snapshot",
                table: "DISBURSEMENT",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Correspondent_swift_bic_snapshot",
                table: "DISBURSEMENT",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency_code_snapshot",
                table: "DISBURSEMENT",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Deductions_snapshot",
                table: "DISBURSEMENT",
                type: "decimal(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Gross_premium_snapshot",
                table: "DISBURSEMENT",
                type: "decimal(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Iban_snapshot",
                table: "DISBURSEMENT",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Panel_reference_snapshot",
                table: "DISBURSEMENT",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reinsurer_address_snapshot",
                table: "DISBURSEMENT",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reinsurer_city_snapshot",
                table: "DISBURSEMENT",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reinsurer_country_snapshot",
                table: "DISBURSEMENT",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reinsurer_name_snapshot",
                table: "DISBURSEMENT",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reinsurer_tax_id_snapshot",
                table: "DISBURSEMENT",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "Started_at",
                table: "DISBURSEMENT",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Started_by",
                table: "DISBURSEMENT",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Swift_bic_snapshot",
                table: "DISBURSEMENT",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Umr_snapshot",
                table: "DISBURSEMENT",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Bank_city_snapshot",
                table: "DISBURSEMENT");

            migrationBuilder.DropColumn(
                name: "Bank_country_snapshot",
                table: "DISBURSEMENT");

            migrationBuilder.DropColumn(
                name: "Bank_name_snapshot",
                table: "DISBURSEMENT");

            migrationBuilder.DropColumn(
                name: "Cedent_name_snapshot",
                table: "DISBURSEMENT");

            migrationBuilder.DropColumn(
                name: "Closed_at",
                table: "DISBURSEMENT");

            migrationBuilder.DropColumn(
                name: "Closed_by",
                table: "DISBURSEMENT");

            migrationBuilder.DropColumn(
                name: "Contract_name_snapshot",
                table: "DISBURSEMENT");

            migrationBuilder.DropColumn(
                name: "Correspondent_bank_name_snapshot",
                table: "DISBURSEMENT");

            migrationBuilder.DropColumn(
                name: "Correspondent_swift_bic_snapshot",
                table: "DISBURSEMENT");

            migrationBuilder.DropColumn(
                name: "Currency_code_snapshot",
                table: "DISBURSEMENT");

            migrationBuilder.DropColumn(
                name: "Deductions_snapshot",
                table: "DISBURSEMENT");

            migrationBuilder.DropColumn(
                name: "Gross_premium_snapshot",
                table: "DISBURSEMENT");

            migrationBuilder.DropColumn(
                name: "Iban_snapshot",
                table: "DISBURSEMENT");

            migrationBuilder.DropColumn(
                name: "Panel_reference_snapshot",
                table: "DISBURSEMENT");

            migrationBuilder.DropColumn(
                name: "Reinsurer_address_snapshot",
                table: "DISBURSEMENT");

            migrationBuilder.DropColumn(
                name: "Reinsurer_city_snapshot",
                table: "DISBURSEMENT");

            migrationBuilder.DropColumn(
                name: "Reinsurer_country_snapshot",
                table: "DISBURSEMENT");

            migrationBuilder.DropColumn(
                name: "Reinsurer_name_snapshot",
                table: "DISBURSEMENT");

            migrationBuilder.DropColumn(
                name: "Reinsurer_tax_id_snapshot",
                table: "DISBURSEMENT");

            migrationBuilder.DropColumn(
                name: "Started_at",
                table: "DISBURSEMENT");

            migrationBuilder.DropColumn(
                name: "Started_by",
                table: "DISBURSEMENT");

            migrationBuilder.DropColumn(
                name: "Swift_bic_snapshot",
                table: "DISBURSEMENT");

            migrationBuilder.DropColumn(
                name: "Umr_snapshot",
                table: "DISBURSEMENT");
        }
    }
}
