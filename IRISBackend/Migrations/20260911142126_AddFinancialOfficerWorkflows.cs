using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialOfficerWorkflows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Id_disbursement",
                table: "REMITTANCE_REF",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "Id_receipt_allocation",
                table: "REMITTANCE_REF",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Id_panel",
                table: "RECEIPT_ALLOCATION",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Id_counterparty",
                table: "INVOICE",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Id_panel",
                table: "INVOICE",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Remittance_ref",
                table: "INVOICE",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NOTIFICATION",
                columns: table => new
                {
                    id_notification = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    recipient_user_id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    notification_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    entity_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    entity_id = table.Column<int>(type: "int", nullable: true),
                    is_read = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysutcdatetime())"),
                    read_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NOTIFICATION", x => x.id_notification);
                });

            migrationBuilder.CreateIndex(
                name: "IX_REMITTANCE_REF_Id_receipt_allocation",
                table: "REMITTANCE_REF",
                column: "Id_receipt_allocation");

            migrationBuilder.CreateIndex(
                name: "IX_RECEIPT_ALLOCATION_Id_panel",
                table: "RECEIPT_ALLOCATION",
                column: "Id_panel");

            migrationBuilder.CreateIndex(
                name: "IX_INVOICE_Id_counterparty",
                table: "INVOICE",
                column: "Id_counterparty");

            migrationBuilder.CreateIndex(
                name: "IX_INVOICE_Id_panel",
                table: "INVOICE",
                column: "Id_panel");

            migrationBuilder.CreateIndex(
                name: "IX_INVOICE_Remittance_ref",
                table: "INVOICE",
                column: "Remittance_ref");

            migrationBuilder.CreateIndex(
                name: "IX_NOTIFICATION_recipient_read",
                table: "NOTIFICATION",
                columns: new[] { "recipient_user_id", "is_read" });

            migrationBuilder.AddForeignKey(
                name: "FK_INVOICE_id_counterparty",
                table: "INVOICE",
                column: "Id_counterparty",
                principalTable: "Counterparties",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_INVOICE_id_panel",
                table: "INVOICE",
                column: "Id_panel",
                principalTable: "CONTRACT_PANEL",
                principalColumn: "Id_panel");

            migrationBuilder.AddForeignKey(
                name: "FK_INVOICE_remittance_ref",
                table: "INVOICE",
                column: "Remittance_ref",
                principalTable: "REMITTANCE_REF",
                principalColumn: "Remittance_ref");

            migrationBuilder.AddForeignKey(
                name: "FK_RECEIPT_ALLOCATION_id_panel",
                table: "RECEIPT_ALLOCATION",
                column: "Id_panel",
                principalTable: "CONTRACT_PANEL",
                principalColumn: "Id_panel");

            migrationBuilder.AddForeignKey(
                name: "FK_REMITTANCE_REF_id_receipt_allocation",
                table: "REMITTANCE_REF",
                column: "Id_receipt_allocation",
                principalTable: "RECEIPT_ALLOCATION",
                principalColumn: "Id_receipt_allocation");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_INVOICE_id_counterparty",
                table: "INVOICE");

            migrationBuilder.DropForeignKey(
                name: "FK_INVOICE_id_panel",
                table: "INVOICE");

            migrationBuilder.DropForeignKey(
                name: "FK_INVOICE_remittance_ref",
                table: "INVOICE");

            migrationBuilder.DropForeignKey(
                name: "FK_RECEIPT_ALLOCATION_id_panel",
                table: "RECEIPT_ALLOCATION");

            migrationBuilder.DropForeignKey(
                name: "FK_REMITTANCE_REF_id_receipt_allocation",
                table: "REMITTANCE_REF");

            migrationBuilder.DropTable(
                name: "NOTIFICATION");

            migrationBuilder.DropIndex(
                name: "IX_REMITTANCE_REF_Id_receipt_allocation",
                table: "REMITTANCE_REF");

            migrationBuilder.DropIndex(
                name: "IX_RECEIPT_ALLOCATION_Id_panel",
                table: "RECEIPT_ALLOCATION");

            migrationBuilder.DropIndex(
                name: "IX_INVOICE_Id_counterparty",
                table: "INVOICE");

            migrationBuilder.DropIndex(
                name: "IX_INVOICE_Id_panel",
                table: "INVOICE");

            migrationBuilder.DropIndex(
                name: "IX_INVOICE_Remittance_ref",
                table: "INVOICE");

            migrationBuilder.DropColumn(
                name: "Id_receipt_allocation",
                table: "REMITTANCE_REF");

            migrationBuilder.DropColumn(
                name: "Id_panel",
                table: "RECEIPT_ALLOCATION");

            migrationBuilder.DropColumn(
                name: "Id_counterparty",
                table: "INVOICE");

            migrationBuilder.DropColumn(
                name: "Id_panel",
                table: "INVOICE");

            migrationBuilder.DropColumn(
                name: "Remittance_ref",
                table: "INVOICE");

            migrationBuilder.AlterColumn<int>(
                name: "Id_disbursement",
                table: "REMITTANCE_REF",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
