using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddOtherDeductionsPercentage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Other_deductions_percentage",
                table: "CONTRACT_PANEL",
                type: "decimal(19,8)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "other_deductions_percentage",
                table: "INVOICE_PANEL_ALLOCATION",
                type: "decimal(19,8)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Other_deductions_percentage", table: "CONTRACT_PANEL");
            migrationBuilder.DropColumn(name: "other_deductions_percentage", table: "INVOICE_PANEL_ALLOCATION");
        }
    }
}
