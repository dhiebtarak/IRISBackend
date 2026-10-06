using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceReceiptAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_RECEIPT_ALLOCATION_target' AND parent_object_id = OBJECT_ID(N'dbo.RECEIPT_ALLOCATION')) ALTER TABLE [dbo].[RECEIPT_ALLOCATION] DROP CONSTRAINT [CK_RECEIPT_ALLOCATION_target];");

            migrationBuilder.AddColumn<int>(
                name: "id_invoice",
                table: "RECEIPT_ALLOCATION",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RECEIPT_ALLOCATION_id_invoice",
                table: "RECEIPT_ALLOCATION",
                column: "id_invoice");

            migrationBuilder.AddForeignKey(
                name: "FK_RECEIPT_ALLOCATION_id_invoice",
                table: "RECEIPT_ALLOCATION",
                column: "id_invoice",
                principalTable: "INVOICE",
                principalColumn: "Id_invoice");

            migrationBuilder.Sql(
                "ALTER TABLE [dbo].[RECEIPT_ALLOCATION] ADD CONSTRAINT [CK_RECEIPT_ALLOCATION_target] CHECK (" +
                "([Target_type] = N'PREMIUM' AND (([premium_ref] IS NOT NULL AND [Id_contract] IS NULL) OR ([premium_ref] IS NULL AND [Id_contract] IS NOT NULL)) AND [claim_ref] IS NULL AND [id_invoice] IS NULL) OR " +
                "([Target_type] = N'CLAIM' AND [claim_ref] IS NOT NULL AND [premium_ref] IS NULL AND [Id_contract] IS NULL AND [id_invoice] IS NULL) OR " +
                "([Target_type] = N'INVOICE' AND [id_invoice] IS NOT NULL AND [premium_ref] IS NULL AND [claim_ref] IS NULL AND [Id_contract] IS NULL));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_RECEIPT_ALLOCATION_target' AND parent_object_id = OBJECT_ID(N'dbo.RECEIPT_ALLOCATION')) ALTER TABLE [dbo].[RECEIPT_ALLOCATION] DROP CONSTRAINT [CK_RECEIPT_ALLOCATION_target];");
            migrationBuilder.Sql(
                "ALTER TABLE [dbo].[RECEIPT_ALLOCATION] ADD CONSTRAINT [CK_RECEIPT_ALLOCATION_target] " +
                "CHECK (([Target_type] = N'PREMIUM' AND (([premium_ref] IS NOT NULL AND [Id_contract] IS NULL) OR ([premium_ref] IS NULL AND [Id_contract] IS NOT NULL)) AND [claim_ref] IS NULL) OR " +
                "([Target_type] = N'CLAIM' AND [claim_ref] IS NOT NULL AND [premium_ref] IS NULL AND [Id_contract] IS NULL));");

            migrationBuilder.DropForeignKey(
                name: "FK_RECEIPT_ALLOCATION_id_invoice",
                table: "RECEIPT_ALLOCATION");

            migrationBuilder.DropIndex(
                name: "IX_RECEIPT_ALLOCATION_id_invoice",
                table: "RECEIPT_ALLOCATION");

            migrationBuilder.DropColumn(
                name: "id_invoice",
                table: "RECEIPT_ALLOCATION");
        }
    }
}
