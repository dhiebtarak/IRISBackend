/* Run from sqlcmd against master with the deployment-only SQLCMD variables set. */
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @DatabaseName sysname = N'$(DB_NAME)';
DECLARE @LoginName sysname = N'$(DB_USER)';
DECLARE @LoginPassword nvarchar(128) = N'$(DB_PASSWORD)';

IF @DatabaseName = N'' OR @DatabaseName LIKE N'%[^A-Za-z0-9_]%' OR
   @LoginName = N'' OR @LoginName LIKE N'%[^A-Za-z0-9_]%' OR
   @LoginPassword = N'' OR CHARINDEX(N'''', @LoginPassword) > 0
    THROW 51000, 'Database provisioning parameters are missing or contain unsupported characters.', 1;

IF DB_ID(@DatabaseName) IS NULL
    EXEC(N'CREATE DATABASE ' + QUOTENAME(@DatabaseName));

IF SUSER_ID(@LoginName) IS NULL
BEGIN
    DECLARE @CreateLogin nvarchar(max) =
        N'CREATE LOGIN ' + QUOTENAME(@LoginName) + N' WITH PASSWORD = N''' +
        REPLACE(@LoginPassword, N'''', N'''''') + N''', CHECK_POLICY = ON;';
    EXEC(@CreateLogin);
END;

DECLARE @CreateUser nvarchar(max) = N'USE ' + QUOTENAME(@DatabaseName) + N';
IF DATABASE_PRINCIPAL_ID(N''' + REPLACE(@LoginName, N'''', N'''''') + N''') IS NULL
    CREATE USER ' + QUOTENAME(@LoginName) + N' FOR LOGIN ' + QUOTENAME(@LoginName) + N';
ALTER ROLE db_datareader ADD MEMBER ' + QUOTENAME(@LoginName) + N';
ALTER ROLE db_datawriter ADD MEMBER ' + QUOTENAME(@LoginName) + N';';
EXEC(@CreateUser);

PRINT N'Runtime database login provisioned with data-reader/writer roles.';
