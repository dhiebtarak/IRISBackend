using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddContractPanelPlacementColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Deductible",
                table: "CONTRACT_PANEL",
                type: "decimal(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Premium_rate_pct",
                table: "CONTRACT_PANEL",
                type: "decimal(19,8)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "CONTRACT_PANEL",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Draft");

            migrationBuilder.AddColumn<decimal>(
                name: "Tsi",
                table: "CONTRACT_PANEL",
                type: "decimal(19,4)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Deductible",
                table: "CONTRACT_PANEL");

            migrationBuilder.DropColumn(
                name: "Premium_rate_pct",
                table: "CONTRACT_PANEL");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "CONTRACT_PANEL");

            migrationBuilder.DropColumn(
                name: "Tsi",
                table: "CONTRACT_PANEL");
        }
    }
}
