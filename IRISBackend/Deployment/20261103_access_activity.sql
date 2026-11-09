-- IRIS Access Activity deployment. Run against the configured DevDB before deploying the application.
-- No startup migration is enabled. Back up the database first and retain this script with the release.
SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.UserLoginAudits', N'U') IS NULL OR OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NULL
    THROW 51000, 'Existing Identity and UserLoginAudits schema is required.', 1;
IF OBJECT_ID(N'dbo.AUTH_SESSION', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.AUTH_SESSION (
  Id uniqueidentifier NOT NULL CONSTRAINT PK_AUTH_SESSION PRIMARY KEY,
  UserId nvarchar(450) NOT NULL,
  SessionIdentifier nvarchar(64) NOT NULL,
  LoginAtUtc datetime2 NOT NULL, LastActivityAtUtc datetime2 NOT NULL,
  LogoutAtUtc datetime2 NULL, ExpiresAtUtc datetime2 NOT NULL,
  RevokedAtUtc datetime2 NULL, RevokedByUserId nvarchar(450) NULL,
  RevocationReason nvarchar(200) NULL, Status nvarchar(20) NOT NULL,
  IpAddress nvarchar(64) NULL, UserAgent nvarchar(512) NULL,
  Browser nvarchar(50) NULL, OperatingSystem nvarchar(50) NULL, DeviceType nvarchar(30) NULL,
  CreatedAtUtc datetime2 NOT NULL, UpdatedAtUtc datetime2 NOT NULL,
  CONSTRAINT CK_AUTH_SESSION_Status CHECK (Status IN ('Active','LoggedOut','Expired','Revoked')),
  CONSTRAINT CK_AUTH_SESSION_Times CHECK (ExpiresAtUtc >= LoginAtUtc AND LastActivityAtUtc >= LoginAtUtc),
  CONSTRAINT FK_AUTH_SESSION_AspNetUsers_UserId FOREIGN KEY(UserId) REFERENCES dbo.AspNetUsers(Id),
  CONSTRAINT FK_AUTH_SESSION_AspNetUsers_RevokedByUserId FOREIGN KEY(RevokedByUserId) REFERENCES dbo.AspNetUsers(Id)
 );
END;
IF COL_LENGTH('dbo.UserLoginAudits','EventType') IS NULL
BEGIN
 ALTER TABLE dbo.UserLoginAudits ADD EventType nvarchar(30) NULL;
 EXEC(N'UPDATE dbo.UserLoginAudits SET EventType = CASE WHEN Succeeded = 1 THEN ''LoginSuccessful'' ELSE ''LoginFailed'' END');
 EXEC(N'ALTER TABLE dbo.UserLoginAudits ALTER COLUMN EventType nvarchar(30) NOT NULL');
 EXEC(N'ALTER TABLE dbo.UserLoginAudits ADD CONSTRAINT DF_UserLoginAudits_EventType DEFAULT ''LoginSuccessful'' FOR EventType');
END;
IF COL_LENGTH('dbo.UserLoginAudits','SessionId') IS NULL
 ALTER TABLE dbo.UserLoginAudits ADD SessionId uniqueidentifier NULL;
IF COL_LENGTH('dbo.UserLoginAudits','ActorUserId') IS NULL
 ALTER TABLE dbo.UserLoginAudits ADD ActorUserId nvarchar(450) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_UserLoginAudits_AUTH_SESSION_SessionId')
 EXEC(N'ALTER TABLE dbo.UserLoginAudits WITH CHECK ADD CONSTRAINT FK_UserLoginAudits_AUTH_SESSION_SessionId FOREIGN KEY(SessionId) REFERENCES dbo.AUTH_SESSION(Id)');
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_UserLoginAudits_AspNetUsers_ActorUserId')
 EXEC(N'ALTER TABLE dbo.UserLoginAudits WITH CHECK ADD CONSTRAINT FK_UserLoginAudits_AspNetUsers_ActorUserId FOREIGN KEY(ActorUserId) REFERENCES dbo.AspNetUsers(Id)');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_AUTH_SESSION_SessionIdentifier' AND object_id=OBJECT_ID('dbo.AUTH_SESSION'))
 EXEC(N'CREATE UNIQUE INDEX IX_AUTH_SESSION_SessionIdentifier ON dbo.AUTH_SESSION (SessionIdentifier)');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_AUTH_SESSION_UserId_LoginAtUtc' AND object_id=OBJECT_ID('dbo.AUTH_SESSION'))
 EXEC(N'CREATE INDEX IX_AUTH_SESSION_UserId_LoginAtUtc ON dbo.AUTH_SESSION (UserId, LoginAtUtc)');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_AUTH_SESSION_Status_ExpiresAtUtc' AND object_id=OBJECT_ID('dbo.AUTH_SESSION'))
 EXEC(N'CREATE INDEX IX_AUTH_SESSION_Status_ExpiresAtUtc ON dbo.AUTH_SESSION (Status, ExpiresAtUtc)');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_AUTH_SESSION_LastActivityAtUtc' AND object_id=OBJECT_ID('dbo.AUTH_SESSION'))
 EXEC(N'CREATE INDEX IX_AUTH_SESSION_LastActivityAtUtc ON dbo.AUTH_SESSION (LastActivityAtUtc)');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_AUTH_SESSION_RevokedAtUtc' AND object_id=OBJECT_ID('dbo.AUTH_SESSION'))
 EXEC(N'CREATE INDEX IX_AUTH_SESSION_RevokedAtUtc ON dbo.AUTH_SESSION (RevokedAtUtc)');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_UserLoginAudits_CreatedAtUtc_EventType' AND object_id=OBJECT_ID('dbo.UserLoginAudits'))
 EXEC(N'CREATE INDEX IX_UserLoginAudits_CreatedAtUtc_EventType ON dbo.UserLoginAudits (CreatedAtUtc, EventType)');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_UserLoginAudits_SessionId_CreatedAtUtc' AND object_id=OBJECT_ID('dbo.UserLoginAudits'))
 EXEC(N'CREATE INDEX IX_UserLoginAudits_SessionId_CreatedAtUtc ON dbo.UserLoginAudits (SessionId, CreatedAtUtc)');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_AUTH_SESSION_LoginAtUtc' AND object_id=OBJECT_ID('dbo.AUTH_SESSION'))
 EXEC(N'CREATE INDEX IX_AUTH_SESSION_LoginAtUtc ON dbo.AUTH_SESSION (LoginAtUtc)');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_UserLoginAudits_UserId_CreatedAtUtc' AND object_id=OBJECT_ID('dbo.UserLoginAudits'))
 EXEC(N'CREATE INDEX IX_UserLoginAudits_UserId_CreatedAtUtc ON dbo.UserLoginAudits (UserId, CreatedAtUtc)');
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
-- Rollback approach: stop the new application and background worker; restore the previous binary.
-- Retain AUTH_SESSION and the nullable audit additions to preserve security evidence. The prior
-- application ignores them; its inserts continue to work through the EventType default.
-- Export and approve deletion of security evidence before any eventual schema removal.
-- Never drop/recreate UserLoginAudits or AspNetUsers as part of rollback.
