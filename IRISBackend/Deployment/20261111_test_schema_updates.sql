-- Complete test deployment: additional contacts, bank identifier lengths, and free-text SWIFT/BIC.
SET XACT_ABORT ON; BEGIN TRANSACTION;
-- Both counterparty contacts and unrestricted-length bank identifiers.
-- Run only against your explicitly selected TEST database after a verified backup.
SET XACT_ABORT ON; BEGIN TRANSACTION;
-- Additive and repeatable. Existing primary email/phone columns remain unchanged.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.Counterparties', N'U') IS NULL
    THROW 51000, 'Counterparties table is missing.', 1;
IF OBJECT_ID(N'dbo.COUNTERPARTY_CONTACT', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.COUNTERPARTY_CONTACT (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_COUNTERPARTY_CONTACT PRIMARY KEY,
        CounterpartyId int NOT NULL,
        SortOrder int NOT NULL,
        Email nvarchar(256) NULL,
        PhoneNumber nvarchar(32) NULL,
        CONSTRAINT FK_COUNTERPARTY_CONTACT_Counterparties_CounterpartyId
            FOREIGN KEY (CounterpartyId) REFERENCES dbo.Counterparties(Id) ON DELETE CASCADE
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.COUNTERPARTY_CONTACT')
    AND name = N'IX_COUNTERPARTY_CONTACT_CounterpartyId_SortOrder')
    CREATE INDEX IX_COUNTERPARTY_CONTACT_CounterpartyId_SortOrder
        ON dbo.COUNTERPARTY_CONTACT(CounterpartyId, SortOrder);
COMMIT TRANSACTION;

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

COMMIT TRANSACTION;

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

COMMIT TRANSACTION;
