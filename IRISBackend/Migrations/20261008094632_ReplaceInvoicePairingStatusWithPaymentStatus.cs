using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceInvoicePairingStatusWithPaymentStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE invoice
SET Recovery_status = CASE
    WHEN COALESCE(allocation.SettledAmount, 0) <= 0 THEN N'Unsettled'
    WHEN COALESCE(allocation.SettledAmount, 0) >= COALESCE(invoice.Gross_premium, 0) THEN N'Settled'
    ELSE N'PartiallySettled'
END
FROM dbo.INVOICE AS invoice
OUTER APPLY (
    SELECT SUM(COALESCE(receiptAllocation.Allocated_amount, 0)) AS SettledAmount
    FROM dbo.RECEIPT_ALLOCATION AS receiptAllocation
    WHERE receiptAllocation.id_invoice = invoice.Id_invoice
) AS allocation;");

            migrationBuilder.AlterColumn<string>(
                name: "Recovery_status",
                table: "INVOICE",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true,
                defaultValue: "Unsettled",
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true,
                oldDefaultValue: "Unpaired");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE dbo.INVOICE
SET Recovery_status = CASE
    WHEN Recovery_status = N'Settled' THEN N'Paired'
    ELSE N'Unpaired'
END
WHERE Recovery_status IN (N'Unsettled', N'PartiallySettled', N'Settled');");

            migrationBuilder.AlterColumn<string>(
                name: "Recovery_status",
                table: "INVOICE",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true,
                defaultValue: "Unpaired",
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true,
                oldDefaultValue: "Unsettled");
        }
    }
}
