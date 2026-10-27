using IRISBackend.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations;

[DbContext(typeof(IRISBackendContext))]
[Migration("20261021160000_AddAvoirProcessingMode")]
public sealed class AddAvoirProcessingMode : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "processing_mode",
            table: "INVOICE",
            type: "nvarchar(40)",
            maxLength: 40,
            nullable: true);

        migrationBuilder.Sql("""
            UPDATE invoice
            SET processing_mode = CASE
                WHEN EXISTS
                (
                    SELECT 1
                    FROM dbo.REMITTANCE_REF remittance
                    WHERE remittance.source_invoice_id = invoice.Id_invoice
                      AND remittance.source_type = N'AVOIR'
                      AND remittance.Id_receipt_allocation IS NULL
                      AND remittance.Id_panel IS NOT NULL
                      AND remittance.id_invoice_allocation IS NOT NULL
                      AND NOT EXISTS
                      (
                          SELECT 1
                          FROM dbo.AVOIR_REFUND_SETTLEMENT settlement
                          WHERE settlement.id_remittance_ref = remittance.Remittance_ref
                      )
                ) THEN N'DIRECT_TO_REMITTANCES'
                ELSE N'PAIR_WITH_ENCAISSEMENT'
            END
            FROM dbo.INVOICE AS invoice
            WHERE UPPER(LTRIM(RTRIM(invoice.Invoice_type))) IN (N'AVOIR', N'CREDIT_NOTE')
              AND invoice.processing_mode IS NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "processing_mode", table: "INVOICE");
    }
}
