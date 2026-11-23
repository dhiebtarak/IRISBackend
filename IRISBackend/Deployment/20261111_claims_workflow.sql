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
