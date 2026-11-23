SET XACT_ABORT ON; BEGIN TRANSACTION;
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

-- Additive, repeatable claims deployment. Review the selected database and take a verified backup first.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.CLAIM',N'U') IS NULL OR OBJECT_ID(N'dbo.COLLECTION_NOTE',N'U') IS NULL
    THROW 51000, 'Required existing claim tables are missing.', 1;
DECLARE @claimColumns TABLE (TableName sysname, ColumnName sysname, Definition nvarchar(200));
INSERT INTO @claimColumns VALUES
 (N'CLAIM',N'Other_fees',N'decimal(19,4) NULL'),
 (N'CLAIM',N'Signed_share_snapshot',N'decimal(19,8) NULL'),
 (N'CLAIM',N'Collection_amount',N'decimal(19,4) NULL'),
 (N'CLAIM',N'Processing_mode',N'nvarchar(40) NULL'),
 (N'CLAIM',N'Collection_reference',N'nvarchar(100) NULL'),
 (N'CLAIM',N'Collection_issued_at',N'datetime2 NULL'),
 (N'CLAIM',N'Collection_issued_by',N'nvarchar(450) NULL'),
 (N'CLAIM',N'Contract_name_snapshot',N'nvarchar(255) NULL'),
 (N'CLAIM',N'Umr_snapshot',N'nvarchar(100) NULL'),
 (N'CLAIM',N'Currency_code_snapshot',N'nvarchar(20) NULL'),
 (N'CLAIM',N'Collection_pdf',N'varbinary(max) NULL'),
 (N'COLLECTION_NOTE',N'Id_panel',N'int NULL'),
 (N'COLLECTION_NOTE',N'Id_currency',N'int NULL'),
 (N'COLLECTION_NOTE',N'Reinsurer_name_snapshot',N'nvarchar(255) NULL');
DECLARE @claimTable sysname, @claimColumn sysname, @claimDefinition nvarchar(200), @claimSql nvarchar(max);
DECLARE claim_columns CURSOR LOCAL FAST_FORWARD FOR SELECT TableName, ColumnName, Definition FROM @claimColumns;
OPEN claim_columns;
FETCH NEXT FROM claim_columns INTO @claimTable,@claimColumn,@claimDefinition;
WHILE @@FETCH_STATUS=0
BEGIN
 IF COL_LENGTH(N'dbo.'+@claimTable,@claimColumn) IS NULL
 BEGIN
  SET @claimSql=N'ALTER TABLE dbo.'+QUOTENAME(@claimTable)+N' ADD '+QUOTENAME(@claimColumn)+N' '+@claimDefinition;
  EXEC sys.sp_executesql @claimSql;
 END;
 FETCH NEXT FROM claim_columns INTO @claimTable,@claimColumn,@claimDefinition;
END;
CLOSE claim_columns;
DEALLOCATE claim_columns;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_COLLECTION_NOTE_CONTRACT_PANEL_Id_panel')
 EXEC(N'ALTER TABLE dbo.COLLECTION_NOTE WITH CHECK ADD CONSTRAINT FK_COLLECTION_NOTE_CONTRACT_PANEL_Id_panel FOREIGN KEY(Id_panel) REFERENCES dbo.CONTRACT_PANEL(Id_panel)');
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_COLLECTION_NOTE_CURRENCY_Id_currency')
 EXEC(N'ALTER TABLE dbo.COLLECTION_NOTE WITH CHECK ADD CONSTRAINT FK_COLLECTION_NOTE_CURRENCY_Id_currency FOREIGN KEY(Id_currency) REFERENCES dbo.CURRENCY(Id_currency)');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.CLAIM') AND name=N'UX_CLAIM_number')
 EXEC(N'CREATE UNIQUE INDEX UX_CLAIM_number ON dbo.CLAIM(Claim_number) WHERE Claim_number IS NOT NULL');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.COLLECTION_NOTE') AND name=N'UX_COLLECTION_NOTE_claim_panel')
 EXEC(N'CREATE UNIQUE INDEX UX_COLLECTION_NOTE_claim_panel ON dbo.COLLECTION_NOTE(Id_claim,Id_panel) WHERE Id_panel IS NOT NULL');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.COLLECTION_NOTE') AND name=N'IX_COLLECTION_NOTE_Id_currency')
 EXEC(N'CREATE INDEX IX_COLLECTION_NOTE_Id_currency ON dbo.COLLECTION_NOTE(Id_currency)');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.COLLECTION_NOTE') AND name=N'IX_COLLECTION_NOTE_Id_panel')
 EXEC(N'CREATE INDEX IX_COLLECTION_NOTE_Id_panel ON dbo.COLLECTION_NOTE(Id_panel)');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.RECEIPT_ALLOCATION') AND name=N'UX_RECEIPT_ALLOCATION_active_claim')
 EXEC(N'CREATE UNIQUE INDEX UX_RECEIPT_ALLOCATION_active_claim ON dbo.RECEIPT_ALLOCATION(Claim_ref) WHERE Claim_ref IS NOT NULL AND is_active=1');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.REMITTANCE_REF') AND name=N'UX_REMITTANCE_REF_active_claim_panel')
 EXEC(N'CREATE UNIQUE INDEX UX_REMITTANCE_REF_active_claim_panel ON dbo.REMITTANCE_REF(Claim_ref,Id_panel) WHERE Claim_ref IS NOT NULL AND Is_active=1');
COMMIT TRANSACTION;

COMMIT TRANSACTION;