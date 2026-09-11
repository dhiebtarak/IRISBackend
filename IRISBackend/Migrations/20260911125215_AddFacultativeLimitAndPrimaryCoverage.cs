using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddFacultativeLimitAndPrimaryCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Limit_of_liability",
                table: "CONTRACT_FAC",
                type: "decimal(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_primary",
                table: "CONTRACT_COVERAGE_SELECTION",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                ;WITH MainCoverRows AS
                (
                    SELECT
                        s.[id_contract_coverage_selection],
                        ROW_NUMBER() OVER
                        (
                            PARTITION BY s.[id_contract]
                            ORDER BY s.[id_contract_coverage_selection]
                        ) AS row_number
                    FROM [CONTRACT_COVERAGE_SELECTION] s
                    INNER JOIN [COVERAGE_ITEM] c
                        ON c.[id_coverage_item] = s.[id_coverage_item]
                    WHERE UPPER(LTRIM(RTRIM(c.[coverage_level]))) = 'MAIN_COVER'
                )
                UPDATE s
                SET [is_primary] = 1
                FROM [CONTRACT_COVERAGE_SELECTION] s
                INNER JOIN MainCoverRows m
                    ON m.[id_contract_coverage_selection] = s.[id_contract_coverage_selection]
                WHERE m.row_number = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Limit_of_liability",
                table: "CONTRACT_FAC");

            migrationBuilder.DropColumn(
                name: "is_primary",
                table: "CONTRACT_COVERAGE_SELECTION");
        }
    }
}
