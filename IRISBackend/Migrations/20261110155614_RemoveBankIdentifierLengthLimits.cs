using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace IRISBackend.Migrations;

public partial class RemoveBankIdentifierLengthLimits : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
-- Repeatable, widening-only change. Preserve existing values and nullability.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @columns TABLE (TableName sysname, ColumnName sysname);
INSERT INTO @columns VALUES
    (N'COUNTERPARTY_BANK_ACCOUNT', N'Iban'),
    (N'COUNTERPARTY_BANK_ACCOUNT', N'Rib'),
    (N'DISBURSEMENT', N'Iban_snapshot');
DECLARE @table sysname, @column sysname, @nullable bit, @length smallint, @type sysname, @sql nvarchar(max);
DECLARE bank_columns CURSOR LOCAL FAST_FORWARD FOR SELECT TableName, ColumnName FROM @columns;
OPEN bank_columns;
FETCH NEXT FROM bank_columns INTO @table, @column;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @length = NULL;
    SELECT @nullable = c.is_nullable, @length = c.max_length, @type = TYPE_NAME(c.system_type_id)
    FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'dbo.' + @table) AND c.name = @column;
    IF @length IS NULL THROW 51000, 'A required bank identifier column is missing.', 1;
    IF @type <> N'nvarchar' THROW 51000, 'Unexpected bank identifier column type.', 1;
    IF @length <> -1
    BEGIN
        SET @sql = N'ALTER TABLE dbo.' + QUOTENAME(@table) + N' ALTER COLUMN ' + QUOTENAME(@column)
            + N' nvarchar(max) ' + CASE WHEN @nullable = 1 THEN N'NULL' ELSE N'NOT NULL' END;
        EXEC sys.sp_executesql @sql;
    END;
    FETCH NEXT FROM bank_columns INTO @table, @column;
END;
CLOSE bank_columns;
DEALLOCATE bank_columns;
COMMIT TRANSACTION;

""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // A rollback must never truncate newly accepted identifiers.
        migrationBuilder.Sql("""
IF EXISTS (SELECT 1 FROM dbo.COUNTERPARTY_BANK_ACCOUNT WHERE DATALENGTH(Iban) > 200 OR DATALENGTH(Rib) > 200)
   OR EXISTS (SELECT 1 FROM dbo.DISBURSEMENT WHERE DATALENGTH(Iban_snapshot) > 200)
    THROW 51000, 'Cannot restore the previous column lengths while longer bank identifiers exist.', 1;
ALTER TABLE dbo.COUNTERPARTY_BANK_ACCOUNT ALTER COLUMN Iban nvarchar(100) NULL;
ALTER TABLE dbo.COUNTERPARTY_BANK_ACCOUNT ALTER COLUMN Rib nvarchar(100) NULL;
ALTER TABLE dbo.DISBURSEMENT ALTER COLUMN Iban_snapshot nvarchar(100) NULL;
""");
    }
}