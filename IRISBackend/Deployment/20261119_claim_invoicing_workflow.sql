-- Claim invoicing additions only. Run AFTER the existing claims/attachments deployments.
-- Preflight: verified target, existing claims/note relationships, and duplicate guards.
-- Take and RESTORE VERIFYONLY a full database backup before execution.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SELECT @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName;
IF DB_NAME() <> N'IRISBackend' THROW 51119,'Wrong database. Expected IRISBackend.',1;
IF OBJECT_ID(N'dbo.CLAIM',N'U') IS NULL OR OBJECT_ID(N'dbo.INVOICE',N'U') IS NULL
 OR OBJECT_ID(N'dbo.COLLECTION_NOTE',N'U') IS NULL OR OBJECT_ID(N'dbo.REMITTANCE_REF',N'U') IS NULL
 THROW 51119,'Required financial tables are missing.',1;
IF COL_LENGTH('dbo.CLAIM','id_currency') IS NULL OR COL_LENGTH('dbo.CLAIM','deductible') IS NULL
 OR COL_LENGTH('dbo.CLAIM','paid_loss') IS NULL OR COL_LENGTH('dbo.COLLECTION_NOTE','Id_panel') IS NULL
 OR COL_LENGTH('dbo.COLLECTION_NOTE','Id_currency') IS NULL
 THROW 51119,'Existing claim workflow columns are missing. Apply the earlier claims deployment first.',1;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lock int;
 EXEC @lock=sys.sp_getapplock @Resource=N'IRIS:Schema:ClaimInvoicing',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
 IF @lock<0 THROW 51119,'Claim invoicing schema deployment is busy.',1;
 -- Id_claim is canonical. Preserve legacy Claim_ref, and stop rather than repair business data.
 IF EXISTS(SELECT 1 FROM dbo.COLLECTION_NOTE n LEFT JOIN dbo.CLAIM c ON c.claim_ref=n.Id_claim
   WHERE c.claim_ref IS NULL OR (n.Claim_ref IS NOT NULL AND n.Claim_ref<>n.Id_claim))
  THROW 51119,'Collection note claim links are invalid. Review existing data before deployment.',1;
 IF EXISTS(SELECT Id_claim,Id_panel FROM dbo.COLLECTION_NOTE WHERE Id_panel IS NOT NULL GROUP BY Id_claim,Id_panel HAVING COUNT(*)>1)
  THROW 51119,'Duplicate claim/panel collection notes exist. Review before deployment.',1;
 IF EXISTS(SELECT 1 FROM dbo.COLLECTION_NOTE n LEFT JOIN dbo.CONTRACT_PANEL p ON p.Id_panel=n.Id_panel LEFT JOIN dbo.CURRENCY c ON c.id_currency=n.Id_currency
   WHERE (n.Id_panel IS NOT NULL AND p.Id_panel IS NULL) OR (n.Id_currency IS NOT NULL AND c.id_currency IS NULL))
  THROW 51119,'Collection note panel or currency links are invalid.',1;
 IF COL_LENGTH('dbo.CLAIM','Invoicing_version') IS NULL ALTER TABLE dbo.CLAIM ADD Invoicing_version bigint NOT NULL CONSTRAINT DF_CLAIM_invoicing_version DEFAULT(0);
 IF COL_LENGTH('dbo.CLAIM','Invoiced_at') IS NULL ALTER TABLE dbo.CLAIM ADD Invoiced_at datetime2 NULL;
 IF COL_LENGTH('dbo.CLAIM','Invoiced_by') IS NULL ALTER TABLE dbo.CLAIM ADD Invoiced_by nvarchar(450) NULL;
 IF COL_LENGTH('dbo.CLAIM','Invoicing_snapshot') IS NULL ALTER TABLE dbo.CLAIM ADD Invoicing_snapshot nvarchar(max) NULL;
 IF COL_LENGTH('dbo.INVOICE','Claim_ref') IS NULL ALTER TABLE dbo.INVOICE ADD Claim_ref int NULL;
 IF COL_LENGTH('dbo.COLLECTION_NOTE','Document_reference') IS NULL ALTER TABLE dbo.COLLECTION_NOTE ADD Document_reference nvarchar(100) NULL;
 IF COL_LENGTH('dbo.REMITTANCE_REF','Id_collection_note') IS NULL ALTER TABLE dbo.REMITTANCE_REF ADD Id_collection_note int NULL;
 -- Dynamic batches allow checks and repeated deployment when columns were just added.
 EXEC(N'IF EXISTS(SELECT 1 FROM dbo.INVOICE i LEFT JOIN dbo.CLAIM c ON c.claim_ref=i.Claim_ref WHERE i.Claim_ref IS NOT NULL AND c.claim_ref IS NULL) THROW 51119,''Invalid invoice claim link.'',1;
 IF EXISTS(SELECT Claim_ref FROM dbo.INVOICE WHERE Claim_ref IS NOT NULL GROUP BY Claim_ref HAVING COUNT(*)>1) THROW 51119,''Duplicate claim Avoirs exist.'',1;
 IF EXISTS(SELECT Document_reference FROM dbo.COLLECTION_NOTE WHERE Document_reference IS NOT NULL GROUP BY Document_reference HAVING COUNT(*)>1) THROW 51119,''Duplicate collection references exist.'',1;
 IF EXISTS(SELECT 1 FROM dbo.REMITTANCE_REF r LEFT JOIN dbo.COLLECTION_NOTE n ON n.Id_collection_note=r.Id_collection_note WHERE r.Id_collection_note IS NOT NULL AND n.Id_collection_note IS NULL) THROW 51119,''Invalid remittance collection note link.'',1;
 IF EXISTS(SELECT Id_collection_note FROM dbo.REMITTANCE_REF WHERE Id_collection_note IS NOT NULL GROUP BY Id_collection_note HAVING COUNT(*)>1) THROW 51119,''Duplicate collection note remittances exist.'',1;');
 IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_INVOICE_CLAIM') EXEC(N'ALTER TABLE dbo.INVOICE WITH CHECK ADD CONSTRAINT FK_INVOICE_CLAIM FOREIGN KEY(Claim_ref) REFERENCES dbo.CLAIM(claim_ref);');
 IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_REMITTANCE_REF_COLLECTION_NOTE') EXEC(N'ALTER TABLE dbo.REMITTANCE_REF WITH CHECK ADD CONSTRAINT FK_REMITTANCE_REF_COLLECTION_NOTE FOREIGN KEY(Id_collection_note) REFERENCES dbo.COLLECTION_NOTE(Id_collection_note);');
 IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.INVOICE') AND name='UX_INVOICE_claim') EXEC(N'CREATE UNIQUE INDEX UX_INVOICE_claim ON dbo.INVOICE(Claim_ref) WHERE Claim_ref IS NOT NULL;');
 IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.COLLECTION_NOTE') AND name='UX_COLLECTION_NOTE_reference') EXEC(N'CREATE UNIQUE INDEX UX_COLLECTION_NOTE_reference ON dbo.COLLECTION_NOTE(Document_reference) WHERE Document_reference IS NOT NULL;');
 IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.COLLECTION_NOTE') AND name='UX_COLLECTION_NOTE_claim_panel') CREATE UNIQUE INDEX UX_COLLECTION_NOTE_claim_panel ON dbo.COLLECTION_NOTE(Id_claim,Id_panel) WHERE Id_panel IS NOT NULL;
 IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.REMITTANCE_REF') AND name='UX_REMITTANCE_REF_collection_note') EXEC(N'CREATE UNIQUE INDEX UX_REMITTANCE_REF_collection_note ON dbo.REMITTANCE_REF(Id_collection_note) WHERE Id_collection_note IS NOT NULL;');
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
-- Verification (also safe as a standalone read-only query).
SELECT OBJECT_NAME(c.object_id) TableName,c.name ColumnName,TYPE_NAME(c.system_type_id) DataType,c.is_nullable
FROM sys.columns c WHERE (c.object_id=OBJECT_ID('dbo.CLAIM') AND c.name LIKE 'Invoic%')
 OR (c.object_id=OBJECT_ID('dbo.INVOICE') AND c.name='Claim_ref')
 OR (c.object_id=OBJECT_ID('dbo.COLLECTION_NOTE') AND c.name='Document_reference')
 OR (c.object_id=OBJECT_ID('dbo.REMITTANCE_REF') AND c.name='Id_collection_note');
SELECT name,is_disabled,is_not_trusted FROM sys.foreign_keys WHERE name IN ('FK_INVOICE_CLAIM','FK_REMITTANCE_REF_COLLECTION_NOTE');
SELECT name,is_unique,is_disabled,filter_definition FROM sys.indexes WHERE name IN ('UX_INVOICE_claim','UX_COLLECTION_NOTE_reference','UX_COLLECTION_NOTE_claim_panel','UX_REMITTANCE_REF_collection_note');
-- Recovery: on failure the transaction rolls back. Do not drop additions after approvals exist.
-- Roll back application images while keeping this additive schema; if full recovery is required,
-- stop api/web and restore the verified backup under a reviewed outage plan (later writes would be lost).
