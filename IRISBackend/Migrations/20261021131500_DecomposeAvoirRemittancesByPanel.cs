using IRISBackend.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations;

[DbContext(typeof(IRISBackendContext))]
[Migration("20261021131500_DecomposeAvoirRemittancesByPanel")]
public sealed class DecomposeAvoirRemittancesByPanel : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "UX_REMITTANCE_REF_allocation_panel",
            table: "REMITTANCE_REF");

        migrationBuilder.CreateIndex(
            name: "UX_REMITTANCE_REF_source_allocation_panel",
            table: "REMITTANCE_REF",
            columns: new[] { "Id_receipt_allocation", "source_invoice_id", "id_invoice_allocation", "Id_panel" },
            unique: true,
            filter: "[source_invoice_id] IS NOT NULL AND [id_invoice_allocation] IS NOT NULL AND [Id_panel] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "UX_REMITTANCE_REF_source_allocation_panel",
            table: "REMITTANCE_REF");

        migrationBuilder.CreateIndex(
            name: "UX_REMITTANCE_REF_allocation_panel",
            table: "REMITTANCE_REF",
            columns: new[] { "Id_receipt_allocation", "Id_panel" },
            unique: true,
            filter: "[Id_receipt_allocation] IS NOT NULL AND [Id_panel] IS NOT NULL");
    }
}
