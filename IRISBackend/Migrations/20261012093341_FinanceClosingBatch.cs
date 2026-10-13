using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations
{
    /// <inheritdoc />
    public partial class FinanceClosingBatch : Migration
    {
        public const string NotificationBackfillSql = """
            UPDATE n SET closing_id = c.Id, entity_type = N'OUTGOING_CLOSING', entity_id = c.Id,
                notification_type = N'CLOSING_DISBURSEMENT_REQUIRED',
                target_route = N'/workspace/reassurance/finance-officer/disbursement-queue?closingId=' + CONVERT(nvarchar(20), c.Id)
            FROM NOTIFICATION n
            JOIN OUTGOING_CLOSING_LINE l ON l.RemittanceId = n.entity_id AND l.IsActive = 1
            JOIN OUTGOING_CLOSING c ON c.Id = l.ClosingId AND c.IsActive = 1
            JOIN AspNetUsers u ON u.Id = n.recipient_user_id
            WHERE n.entity_type = N'REMITTANCE_REF' AND n.closing_id IS NULL
                AND LOWER(LTRIM(RTRIM(u.Position))) = N'finance officer';
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IdDisbursement",
                table: "OUTGOING_CLOSING",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "OUTGOING_CLOSING",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "AwaitingFinanceDisbursement");

            migrationBuilder.AddColumn<int>(
                name: "closing_id",
                table: "NOTIFICATION",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OUTGOING_CLOSING_IdDisbursement",
                table: "OUTGOING_CLOSING",
                column: "IdDisbursement",
                unique: true,
                filter: "[IdDisbursement] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NOTIFICATION_closing_id",
                table: "NOTIFICATION",
                column: "closing_id");

            migrationBuilder.AddForeignKey(
                name: "FK_NOTIFICATION_OUTGOING_CLOSING_closing_id",
                table: "NOTIFICATION",
                column: "closing_id",
                principalTable: "OUTGOING_CLOSING",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_OUTGOING_CLOSING_DISBURSEMENT_IdDisbursement",
                table: "OUTGOING_CLOSING",
                column: "IdDisbursement",
                principalTable: "DISBURSEMENT",
                principalColumn: "Id_disbursement");

            migrationBuilder.Sql("""
                UPDATE c SET Status = CASE
                    WHEN NOT EXISTS (SELECT 1 FROM OUTGOING_CLOSING_LINE l JOIN REMITTANCE_REF r ON r.Remittance_ref = l.RemittanceId
                        WHERE l.ClosingId = c.Id AND l.IsActive = 1 AND r.Status <> N'Closed') THEN N'Closed'
                    WHEN EXISTS (SELECT 1 FROM OUTGOING_CLOSING_LINE l JOIN REMITTANCE_REF r ON r.Remittance_ref = l.RemittanceId
                        WHERE l.ClosingId = c.Id AND l.IsActive = 1 AND r.Id_disbursement IS NOT NULL) THEN N'PartiallyDisbursed'
                    ELSE N'AwaitingFinanceDisbursement' END
                FROM OUTGOING_CLOSING c
                WHERE EXISTS (SELECT 1 FROM OUTGOING_CLOSING_LINE l WHERE l.ClosingId = c.Id AND l.IsActive = 1);
                """);
            migrationBuilder.Sql(NotificationBackfillSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_NOTIFICATION_OUTGOING_CLOSING_closing_id",
                table: "NOTIFICATION");

            migrationBuilder.DropForeignKey(
                name: "FK_OUTGOING_CLOSING_DISBURSEMENT_IdDisbursement",
                table: "OUTGOING_CLOSING");

            migrationBuilder.DropIndex(
                name: "IX_OUTGOING_CLOSING_IdDisbursement",
                table: "OUTGOING_CLOSING");

            migrationBuilder.DropIndex(
                name: "IX_NOTIFICATION_closing_id",
                table: "NOTIFICATION");

            migrationBuilder.DropColumn(
                name: "IdDisbursement",
                table: "OUTGOING_CLOSING");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "OUTGOING_CLOSING");

            migrationBuilder.DropColumn(
                name: "closing_id",
                table: "NOTIFICATION");
        }
    }
}
