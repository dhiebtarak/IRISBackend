using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IRISBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddIrisBankAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IRIS_BANK_ACCOUNT",
                columns: table => new
                {
                    id_iris_bank_account = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    bank_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    id_currency = table.Column<int>(type: "int", nullable: false),
                    iban = table.Column<string>(type: "nvarchar(34)", maxLength: 34, nullable: false),
                    bic_swift = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: false),
                    display_order = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    created_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IRIS_BANK_ACCOUNT", x => x.id_iris_bank_account);
                    table.ForeignKey(
                        name: "FK_IRIS_BANK_ACCOUNT_CURRENCY",
                        column: x => x.id_currency,
                        principalTable: "CURRENCY",
                        principalColumn: "Id_currency",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IRIS_BANK_ACCOUNT_id_currency",
                table: "IRIS_BANK_ACCOUNT",
                column: "id_currency");

            migrationBuilder.CreateIndex(
                name: "IX_IRIS_BANK_ACCOUNT_is_active",
                table: "IRIS_BANK_ACCOUNT",
                column: "is_active");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IRIS_BANK_ACCOUNT");
        }
    }
}
