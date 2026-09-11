using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddCounterpartyTypeAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "COUNTERPARTY_TYPE_ASSIGNMENT",
                columns: table => new
                {
                    CounterpartyId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_COUNTERPARTY_TYPE_ASSIGNMENT", x => new { x.CounterpartyId, x.Type });
                    table.ForeignKey(
                        name: "FK_COUNTERPARTY_TYPE_ASSIGNMENT_Counterparty",
                        column: x => x.CounterpartyId,
                        principalTable: "Counterparties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_COUNTERPARTY_TYPE_ASSIGNMENT_Type",
                table: "COUNTERPARTY_TYPE_ASSIGNMENT",
                column: "Type");

            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1
                    FROM [Counterparties]
                    WHERE NULLIF(LTRIM(RTRIM([Type])), '') IS NOT NULL
                      AND LOWER(LTRIM(RTRIM([Type]))) NOT IN ('cedent', 'reinsurer', 'client', 'partner')
                )
                    THROW 51000, 'Unsupported legacy counterparty type found in Counterparties.Type.', 1;

                INSERT INTO [COUNTERPARTY_TYPE_ASSIGNMENT] ([CounterpartyId], [Type])
                SELECT c.[Id],
                       CASE LOWER(LTRIM(RTRIM(c.[Type])))
                           WHEN 'cedent' THEN N'Cedent'
                           WHEN 'reinsurer' THEN N'Reinsurer'
                           WHEN 'client' THEN N'Client'
                           WHEN 'partner' THEN N'Partner'
                       END
                FROM [Counterparties] c
                WHERE NULLIF(LTRIM(RTRIM(c.[Type])), '') IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "COUNTERPARTY_TYPE_ASSIGNMENT");
        }
    }
}
