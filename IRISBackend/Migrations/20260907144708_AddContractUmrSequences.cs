using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations
{
    public partial class AddContractUmrSequences : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CONTRACT_UMR_SEQUENCE",
                columns: table => new
                {
                    inception_year = table.Column<int>(type: "int", nullable: false),
                    contract_type_code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    main_cover_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    current_sequence = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_CONTRACT_UMR_SEQUENCE",
                        x => new { x.inception_year, x.contract_type_code, x.main_cover_code });
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CONTRACT_UMR_SEQUENCE");
        }
    }
}
