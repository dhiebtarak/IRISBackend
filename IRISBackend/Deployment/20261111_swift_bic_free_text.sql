-- Repeatable widening-only update; preserves existing values and nullability.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @swiftColumns TABLE (TableName sysname, ColumnName sysname);
INSERT INTO @swiftColumns VALUES
    (N'COUNTERPARTY_BANK_ACCOUNT', N'Swift_bic'),
    (N'COUNTERPARTY_BANK_ACCOUNT', N'Correspondent_swift_bic'),
    (N'DISBURSEMENT', N'Swift_bic_snapshot'),
    (N'DISBURSEMENT', N'Correspondent_swift_bic_snapshot');
DECLARE @swiftTable sysname, @swiftColumn sysname, @swiftNullable bit, @swiftLength smallint, @swiftType sysname, @swiftSql nvarchar(max);
DECLARE swift_columns CURSOR LOCAL FAST_FORWARD FOR SELECT TableName, ColumnName FROM @swiftColumns;
OPEN swift_columns;
FETCH NEXT FROM swift_columns INTO @swiftTable, @swiftColumn;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @swiftLength = NULL;
    SELECT @swiftNullable = c.is_nullable, @swiftLength = c.max_length, @swiftType = TYPE_NAME(c.system_type_id)
    FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'dbo.' + @swiftTable) AND c.name = @swiftColumn;
    IF @swiftLength IS NULL THROW 51000, 'A required SWIFT/BIC column is missing.', 1;
    IF @swiftType <> N'nvarchar' THROW 51000, 'Unexpected SWIFT/BIC column type.', 1;
    IF @swiftLength <> -1
    BEGIN
        SET @swiftSql = N'ALTER TABLE dbo.' + QUOTENAME(@swiftTable) + N' ALTER COLUMN ' + QUOTENAME(@swiftColumn)
            + N' nvarchar(max) ' + CASE WHEN @swiftNullable = 1 THEN N'NULL' ELSE N'NOT NULL' END;
        EXEC sys.sp_executesql @swiftSql;
    END;
    FETCH NEXT FROM swift_columns INTO @swiftTable, @swiftColumn;
END;
CLOSE swift_columns;
DEALLOCATE swift_columns;
COMMIT TRANSACTION;
