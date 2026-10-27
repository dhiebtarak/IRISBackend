using IRISBackend.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations;

[DbContext(typeof(IRISBackendContext))]
[Migration("20261023101500_CorrectDraftInvoiceBrokerageFromGross")]
public sealed class CorrectDraftInvoiceBrokerageFromGross : Migration
{
    public const string RepairSql = """
        IF OBJECT_ID(N'dbo.INVOICE_PANEL_BROKERAGE_RECONCILIATION', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.INVOICE_PANEL_BROKERAGE_RECONCILIATION
            (
                id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_INVOICE_PANEL_BROKERAGE_RECONCILIATION PRIMARY KEY,
                id_invoice int NOT NULL,
                id_invoice_allocation int NOT NULL,
                invoice_number nvarchar(100) NOT NULL,
                invoice_status nvarchar(50) NOT NULL,
                old_brokerage_amount decimal(19,4) NOT NULL,
                expected_brokerage_amount decimal(19,4) NOT NULL,
                old_net_payable_amount decimal(19,4) NOT NULL,
                expected_net_payable_amount decimal(19,4) NOT NULL,
                correction_action nvarchar(50) NOT NULL,
                recorded_at_utc datetime2 NOT NULL CONSTRAINT DF_INVOICE_PANEL_BROKERAGE_RECONCILIATION_recorded_at DEFAULT SYSUTCDATETIME()
            );
            CREATE UNIQUE INDEX UX_INVOICE_PANEL_BROKERAGE_RECONCILIATION_allocation
                ON dbo.INVOICE_PANEL_BROKERAGE_RECONCILIATION(id_invoice_allocation);
        END;

        WITH candidates AS
        (
            SELECT
                invoice.Id_invoice AS invoice_id,
                allocation.id_invoice_allocation AS allocation_id,
                invoice.Invoice_number AS invoice_number,
                invoice.Status AS invoice_status,
                allocation.brokerage_amount AS old_brokerage,
                allocation.net_amount - allocation.brokerage_amount - allocation.other_deductions AS old_payable,
                ROUND(allocation.gross_premium * COALESCE(allocation.brokerage_pct, 0) / 100.0,
                    CASE WHEN currency.Decimal_places IS NULL THEN 2
                         WHEN currency.Decimal_places < 0 THEN 0
                         WHEN currency.Decimal_places > 4 THEN 4
                         ELSE currency.Decimal_places END) AS expected_brokerage,
                CASE
                    WHEN UPPER(LTRIM(RTRIM(invoice.Status))) = N'DRAFT'
                     AND NOT EXISTS
                     (
                         SELECT 1 FROM dbo.RECEIPT_ALLOCATION receiptAllocation
                         WHERE receiptAllocation.id_invoice = invoice.Id_invoice
                           AND receiptAllocation.is_active = 1
                     )
                     AND NOT EXISTS
                     (
                         SELECT 1 FROM dbo.REMITTANCE_REF remittance
                         WHERE remittance.source_invoice_id = invoice.Id_invoice
                           AND remittance.id_invoice_allocation = allocation.id_invoice_allocation
                     )
                    THEN N'CORRECTED_DRAFT'
                    WHEN UPPER(LTRIM(RTRIM(invoice.Status))) = N'DRAFT'
                    THEN N'RECONCILE_DRAFT_WITH_FINANCIAL_LINKS'
                    ELSE N'RECONCILE_FINALIZED'
                END AS correction_action
            FROM dbo.INVOICE invoice
            INNER JOIN dbo.INVOICE_PANEL_ALLOCATION allocation
                ON allocation.id_invoice = invoice.Id_invoice
            INNER JOIN dbo.CURRENCY currency
                ON currency.Id_currency = invoice.Id_currency
            WHERE allocation.Brokerage_amount <> ROUND(
                    allocation.gross_premium * COALESCE(allocation.brokerage_pct, 0) / 100.0,
                    CASE WHEN currency.Decimal_places IS NULL THEN 2
                         WHEN currency.Decimal_places < 0 THEN 0
                         WHEN currency.Decimal_places > 4 THEN 4
                         ELSE currency.Decimal_places END)
              AND allocation.Brokerage_amount = ROUND(
                    allocation.net_amount * COALESCE(allocation.brokerage_pct, 0) / 100.0,
                    CASE WHEN currency.Decimal_places IS NULL THEN 2
                         WHEN currency.Decimal_places < 0 THEN 0
                         WHEN currency.Decimal_places > 4 THEN 4
                         ELSE currency.Decimal_places END)
        )
        INSERT INTO dbo.INVOICE_PANEL_BROKERAGE_RECONCILIATION
        (
            id_invoice, id_invoice_allocation, invoice_number, invoice_status,
            old_brokerage_amount, expected_brokerage_amount,
            old_net_payable_amount, expected_net_payable_amount, correction_action
        )
        SELECT candidate.invoice_id, candidate.allocation_id, candidate.invoice_number, candidate.invoice_status,
               candidate.old_brokerage, candidate.expected_brokerage, candidate.old_payable,
               candidate.old_payable + candidate.old_brokerage - candidate.expected_brokerage,
               candidate.correction_action
        FROM candidates candidate
        WHERE NOT EXISTS
        (
            SELECT 1 FROM dbo.INVOICE_PANEL_BROKERAGE_RECONCILIATION audit
            WHERE audit.id_invoice_allocation = candidate.allocation_id
        );

        UPDATE allocation
        SET allocation.Brokerage_amount = audit.expected_brokerage_amount
        FROM dbo.INVOICE_PANEL_ALLOCATION allocation
        INNER JOIN dbo.INVOICE_PANEL_BROKERAGE_RECONCILIATION audit
            ON audit.id_invoice_allocation = allocation.id_invoice_allocation
        WHERE audit.correction_action = N'CORRECTED_DRAFT';
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(RepairSql);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Keep the audit and corrected financial values; reversing them would reintroduce the defect.
    }
}
