using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations
{
    /// <inheritdoc />
    public partial class AllowContractReceiptAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE [RECEIPT_ALLOCATION] DROP CONSTRAINT [CK_RECEIPT_ALLOCATION_target];");

            migrationBuilder.Sql(
                "ALTER TABLE [RECEIPT_ALLOCATION] ADD CONSTRAINT [CK_RECEIPT_ALLOCATION_target] " +
                "CHECK (([target_type] = N'PREMIUM' AND " +
                "(([premium_ref] IS NOT NULL AND [Id_contract] IS NULL) OR " +
                "([premium_ref] IS NULL AND [Id_contract] IS NOT NULL)) AND [claim_ref] IS NULL) OR " +
                "([target_type] = N'CLAIM' AND [claim_ref] IS NOT NULL AND " +
                "[premium_ref] IS NULL AND [Id_contract] IS NULL));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE [RECEIPT_ALLOCATION] DROP CONSTRAINT [CK_RECEIPT_ALLOCATION_target];");

            migrationBuilder.Sql(
                "ALTER TABLE [RECEIPT_ALLOCATION] ADD CONSTRAINT [CK_RECEIPT_ALLOCATION_target] " +
                "CHECK (([target_type] = N'PREMIUM' AND [premium_ref] IS NOT NULL AND [claim_ref] IS NULL) OR " +
                "([target_type] = N'CLAIM' AND [claim_ref] IS NOT NULL AND [premium_ref] IS NULL));");
        }
    }
}
