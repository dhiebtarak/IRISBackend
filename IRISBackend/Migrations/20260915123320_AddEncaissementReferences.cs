using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddEncaissementReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "created_by",
                table: "NOTIFICATION",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recipient_role",
                table: "NOTIFICATION",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "target_route",
                table: "NOTIFICATION",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Business_reference",
                table: "BANK_RECEIPT",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "Declared_invoice_month",
                table: "BANK_RECEIPT",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Declared_invoice_number",
                table: "BANK_RECEIPT",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "created_by",
                table: "NOTIFICATION");

            migrationBuilder.DropColumn(
                name: "recipient_role",
                table: "NOTIFICATION");

            migrationBuilder.DropColumn(
                name: "target_route",
                table: "NOTIFICATION");

            migrationBuilder.DropColumn(
                name: "Business_reference",
                table: "BANK_RECEIPT");

            migrationBuilder.DropColumn(
                name: "Declared_invoice_month",
                table: "BANK_RECEIPT");

            migrationBuilder.DropColumn(
                name: "Declared_invoice_number",
                table: "BANK_RECEIPT");
        }
    }
}
