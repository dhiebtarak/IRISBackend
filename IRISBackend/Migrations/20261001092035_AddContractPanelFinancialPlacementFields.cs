using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddContractPanelFinancialPlacementFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Brokerage_iris_amount",
                table: "CONTRACT_PANEL",
                type: "decimal(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Cedent_commission_amount",
                table: "CONTRACT_PANEL",
                type: "decimal(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Cedent_commission_pct",
                table: "CONTRACT_PANEL",
                type: "decimal(19,8)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Deductible_amount",
                table: "CONTRACT_PANEL",
                type: "decimal(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Deductible_type",
                table: "CONTRACT_PANEL",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Deductible_value",
                table: "CONTRACT_PANEL",
                type: "decimal(19,8)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Limit_of_liability_amount",
                table: "CONTRACT_PANEL",
                type: "decimal(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Limit_of_liability_type",
                table: "CONTRACT_PANEL",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Limit_of_liability_value",
                table: "CONTRACT_PANEL",
                type: "decimal(19,8)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Brokerage_iris_amount",
                table: "CONTRACT_PANEL");

            migrationBuilder.DropColumn(
                name: "Cedent_commission_amount",
                table: "CONTRACT_PANEL");

            migrationBuilder.DropColumn(
                name: "Cedent_commission_pct",
                table: "CONTRACT_PANEL");

            migrationBuilder.DropColumn(
                name: "Deductible_amount",
                table: "CONTRACT_PANEL");

            migrationBuilder.DropColumn(
                name: "Deductible_type",
                table: "CONTRACT_PANEL");

            migrationBuilder.DropColumn(
                name: "Deductible_value",
                table: "CONTRACT_PANEL");

            migrationBuilder.DropColumn(
                name: "Limit_of_liability_amount",
                table: "CONTRACT_PANEL");

            migrationBuilder.DropColumn(
                name: "Limit_of_liability_type",
                table: "CONTRACT_PANEL");

            migrationBuilder.DropColumn(
                name: "Limit_of_liability_value",
                table: "CONTRACT_PANEL");
        }
    }
}
