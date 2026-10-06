using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations
{
    /// <inheritdoc />
    public partial class SeparateCedentPremiumInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "INVOICE_NUMBER_SEQUENCE",
                columns: table => new
                {
                    invoice_year = table.Column<int>(type: "int", nullable: false),
                    last_sequence = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_INVOICE_NUMBER_SEQUENCE", x => x.invoice_year);
                });

            migrationBuilder.Sql("""
                SELECT
                    invoice.Id_invoice,
                    YEAR(invoice.Invoice_date) AS invoice_year,
                    COALESCE(existing.last_sequence, 0) + ROW_NUMBER() OVER (
                        PARTITION BY YEAR(invoice.Invoice_date)
                        ORDER BY invoice.Id_invoice
                    ) AS assigned_sequence
                INTO #misclassified_invoices
                FROM dbo.INVOICE AS invoice
                OUTER APPLY
                (
                    SELECT MAX(TRY_CONVERT(int, RIGHT(other.Invoice_number, 6))) AS last_sequence
                    FROM dbo.INVOICE AS other
                    WHERE TRY_CONVERT(int, SUBSTRING(other.Invoice_number, 5, 4)) = YEAR(invoice.Invoice_date)
                      AND other.Invoice_type <> N'CEDENT_PREMIUM_DEBIT_NOTE'
                      AND other.Invoice_number LIKE N'INV-[0-9][0-9][0-9][0-9]-[0-9][0-9][0-9][0-9][0-9][0-9]'
                ) AS existing
                WHERE invoice.Invoice_type = N'CEDENT_PREMIUM_DEBIT_NOTE';

                IF EXISTS (SELECT 1 FROM #misclassified_invoices WHERE assigned_sequence > 999999)
                    THROW 51001, 'The annual invoice number sequence exceeds six digits.', 1;

                UPDATE invoice
                SET Invoice_type = N'CEDENT_PREMIUM_INVOICE',
                    Invoice_number = CONCAT(
                        N'INV-', misclassified.invoice_year, N'-',
                        RIGHT(CONCAT(N'000000', misclassified.assigned_sequence), 6)
                    )
                FROM dbo.INVOICE AS invoice
                INNER JOIN #misclassified_invoices AS misclassified
                    ON misclassified.Id_invoice = invoice.Id_invoice;

                INSERT INTO dbo.INVOICE_NUMBER_SEQUENCE (invoice_year, last_sequence)
                SELECT TRY_CONVERT(int, SUBSTRING(invoice.Invoice_number, 5, 4)), MAX(TRY_CONVERT(int, RIGHT(invoice.Invoice_number, 6)))
                FROM dbo.INVOICE AS invoice
                WHERE invoice.Invoice_number LIKE N'INV-[0-9][0-9][0-9][0-9]-[0-9][0-9][0-9][0-9][0-9][0-9]'
                GROUP BY TRY_CONVERT(int, SUBSTRING(invoice.Invoice_number, 5, 4));

                DROP TABLE #misclassified_invoices;
                """);

            migrationBuilder.CreateIndex(
                name: "UX_INVOICE_invoice_number",
                table: "INVOICE",
                column: "Invoice_number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_INVOICE_invoice_number",
                table: "INVOICE");

            migrationBuilder.DropTable(name: "INVOICE_NUMBER_SEQUENCE");
        }
    }
}
