using IRISBackend.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace IRISBackend.Migrations;

[DbContext(typeof(IRISBackendContext))]
[Migration("20261023120000_AllowIncompleteFacultativeDrafts")]
public sealed class AllowIncompleteFacultativeDrafts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "Name", table: "CONTRACT", type: "nvarchar(255)", maxLength: 255,
            nullable: true, oldClrType: typeof(string), oldType: "nvarchar(255)", oldMaxLength: 255);
        migrationBuilder.AlterColumn<int>(
            name: "Id_counterparty", table: "CONTRACT", type: "int", nullable: true,
            oldClrType: typeof(int), oldType: "int");
        migrationBuilder.AlterColumn<int>(
            name: "Id_currency", table: "CONTRACT", type: "int", nullable: true,
            oldClrType: typeof(int), oldType: "int");
        migrationBuilder.AlterColumn<DateOnly>(
            name: "Inception_date", table: "CONTRACT", type: "date", nullable: true,
            oldClrType: typeof(DateOnly), oldType: "date");
        migrationBuilder.AlterColumn<DateOnly>(
            name: "Expiry_date", table: "CONTRACT", type: "date", nullable: true,
            oldClrType: typeof(DateOnly), oldType: "date");
        migrationBuilder.AlterColumn<bool>(
            name: "Is_fronting", table: "CONTRACT", type: "bit", nullable: true,
            oldClrType: typeof(bool), oldType: "bit");
        migrationBuilder.AlterColumn<bool>(
            name: "Flag", table: "CONTRACT", type: "bit", nullable: true,
            oldClrType: typeof(bool), oldType: "bit");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "Name", table: "CONTRACT", type: "nvarchar(255)", maxLength: 255,
            nullable: false, oldClrType: typeof(string), oldType: "nvarchar(255)", oldMaxLength: 255, oldNullable: true);
        migrationBuilder.AlterColumn<int>(
            name: "Id_counterparty", table: "CONTRACT", type: "int", nullable: false,
            oldClrType: typeof(int), oldType: "int", oldNullable: true);
        migrationBuilder.AlterColumn<int>(
            name: "Id_currency", table: "CONTRACT", type: "int", nullable: false,
            oldClrType: typeof(int), oldType: "int", oldNullable: true);
        migrationBuilder.AlterColumn<DateOnly>(
            name: "Inception_date", table: "CONTRACT", type: "date", nullable: false,
            oldClrType: typeof(DateOnly), oldType: "date", oldNullable: true);
        migrationBuilder.AlterColumn<DateOnly>(
            name: "Expiry_date", table: "CONTRACT", type: "date", nullable: false,
            oldClrType: typeof(DateOnly), oldType: "date", oldNullable: true);
        migrationBuilder.AlterColumn<bool>(
            name: "Is_fronting", table: "CONTRACT", type: "bit", nullable: false,
            oldClrType: typeof(bool), oldType: "bit", oldNullable: true);
        migrationBuilder.AlterColumn<bool>(
            name: "Flag", table: "CONTRACT", type: "bit", nullable: false,
            oldClrType: typeof(bool), oldType: "bit", oldNullable: true);
    }
}
