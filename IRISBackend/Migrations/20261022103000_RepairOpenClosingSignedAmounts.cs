using IRISBackend.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations;

[DbContext(typeof(IRISBackendContext))]
[Migration("20261022103000_RepairOpenClosingSignedAmounts")]
public sealed class RepairOpenClosingSignedAmounts : Migration
{
    public const string RepairSql = """
        UPDATE line
        SET PayableAmount = ROUND(ABS(COALESCE(remittance.Net_payable_amount, 0)),
                CASE WHEN currency.Decimal_places IS NULL THEN 2
                     WHEN currency.Decimal_places < 0 THEN 0
                     WHEN currency.Decimal_places > 4 THEN 4
                     ELSE currency.Decimal_places END)
            * CASE WHEN UPPER(LTRIM(RTRIM(remittance.Source_type))) = N'AVOIR' THEN -1 ELSE 1 END
        FROM dbo.OUTGOING_CLOSING_LINE AS line
        INNER JOIN dbo.OUTGOING_CLOSING AS closing ON closing.Id = line.ClosingId
        INNER JOIN dbo.REMITTANCE_REF AS remittance ON remittance.Remittance_ref = line.RemittanceId
        INNER JOIN dbo.CURRENCY AS currency ON currency.Id_currency = closing.IdCurrency
        WHERE closing.IsActive = 1
          AND line.IsActive = 1
          AND closing.IdDisbursement IS NULL
          AND closing.Status IN (N'Draft', N'AwaitingFinanceDisbursement')
          AND UPPER(LTRIM(RTRIM(remittance.Source_type))) IN (N'PREMIUM', N'AVOIR');

        UPDATE closing
        SET TotalAmount = ROUND(totals.CalculatedTotal,
                CASE WHEN currency.Decimal_places IS NULL THEN 2
                     WHEN currency.Decimal_places < 0 THEN 0
                     WHEN currency.Decimal_places > 4 THEN 4
                     ELSE currency.Decimal_places END)
        FROM dbo.OUTGOING_CLOSING AS closing
        INNER JOIN dbo.CURRENCY AS currency ON currency.Id_currency = closing.IdCurrency
        CROSS APPLY
        (
            SELECT SUM(line.PayableAmount) AS CalculatedTotal
            FROM dbo.OUTGOING_CLOSING_LINE AS line
            WHERE line.ClosingId = closing.Id AND line.IsActive = 1
        ) AS totals
        WHERE closing.IsActive = 1
          AND closing.IdDisbursement IS NULL
          AND closing.Status IN (N'Draft', N'AwaitingFinanceDisbursement')
          AND totals.CalculatedTotal IS NOT NULL;
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(RepairSql);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // A data normalization cannot be safely reversed without a historical value snapshot.
    }
}
