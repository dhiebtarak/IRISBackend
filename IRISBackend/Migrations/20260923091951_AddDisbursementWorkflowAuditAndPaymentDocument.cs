using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddDisbursementWorkflowAuditAndPaymentDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "Confirmed_at",
                table: "REMITTANCE_REF",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Confirmed_by",
                table: "REMITTANCE_REF",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Id_disbursement",
                table: "INVOICE",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Bank_reference",
                table: "DISBURSEMENT",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_INVOICE_id_disbursement",
                table: "INVOICE",
                column: "Id_disbursement",
                unique: true,
                filter: "[Id_disbursement] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_INVOICE_id_disbursement",
                table: "INVOICE",
                column: "Id_disbursement",
                principalTable: "DISBURSEMENT",
                principalColumn: "Id_disbursement");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_INVOICE_id_disbursement",
                table: "INVOICE");

            migrationBuilder.DropIndex(
                name: "UX_INVOICE_id_disbursement",
                table: "INVOICE");

            migrationBuilder.DropColumn(
                name: "Confirmed_at",
                table: "REMITTANCE_REF");

            migrationBuilder.DropColumn(
                name: "Confirmed_by",
                table: "REMITTANCE_REF");

            migrationBuilder.DropColumn(
                name: "Id_disbursement",
                table: "INVOICE");

            migrationBuilder.DropColumn(
                name: "Bank_reference",
                table: "DISBURSEMENT");
        }
    }
}
