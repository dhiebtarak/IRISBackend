using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations
{
    /// <inheritdoc />
    public partial class BackfillInvoiceRecoveryStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE dbo.INVOICE
SET Recovery_status = N'Unpaired'
WHERE Recovery_status IS NULL
   OR LTRIM(RTRIM(Recovery_status)) = N'';");

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
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Recovery_status",
                table: "INVOICE",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true,
                oldDefaultValue: "Unpaired");
        }
    }
}
