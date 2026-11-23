# Manual hosted schema deployment. Does not deploy application images.
[CmdletBinding()]
param([string]$Distribution = 'Ubuntu-24.04')
$ErrorActionPreference = 'Stop'
$claimDeployment = @'
set -euo pipefail
cd /home/iris-machine/optima-rea/IRISBackend/deploy
script=../IRISBackend/Deployment/20261119_claim_invoicing_workflow.sql
test -s "$script"
docker compose exec -T db sh -c 'test "$DB_NAME" = IRISBackend'
# Quiesce writes before backup; SQL Server stays running.
docker compose stop web api
backup="/var/opt/mssql/data/IRISBackend_before_claim_invoicing_$(date -u +%Y%m%d_%H%M%S).bak"
printf "BACKUP DATABASE [IRISBackend] TO DISK=N'%s' WITH COPY_ONLY,CHECKSUM; RESTORE VERIFYONLY FROM DISK=N'%s' WITH CHECKSUM;\n" "$backup" "$backup" |
  docker compose exec -T db sh -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -d "$DB_NAME" -b -V 16 -f 65001'
echo "Verified backup in persistent sql_data volume: $backup"
cat "$script" |
  docker compose exec -T db sh -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -d "$DB_NAME" -b -V 16 -f 65001'
cat <<'SQL' | docker compose exec -T db sh -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -d "$DB_NAME" -b -V 16'
SET NOCOUNT ON;
IF DB_NAME()<>N'IRISBackend' THROW 51119,'Unexpected database.',1;
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.CLAIM') AND name IN ('Invoicing_version','Invoiced_at','Invoiced_by','Invoicing_snapshot'))<>4 THROW 51119,'Claim columns missing.',1;
IF COL_LENGTH('dbo.INVOICE','Claim_ref') IS NULL OR COL_LENGTH('dbo.COLLECTION_NOTE','Document_reference') IS NULL OR COL_LENGTH('dbo.REMITTANCE_REF','Id_collection_note') IS NULL THROW 51119,'Document links missing.',1;
IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE name IN ('FK_INVOICE_CLAIM','FK_REMITTANCE_REF_COLLECTION_NOTE') AND is_disabled=0 AND is_not_trusted=0)<>2 THROW 51119,'Document foreign keys missing or untrusted.',1;
IF (SELECT COUNT(*) FROM sys.indexes WHERE name IN ('UX_INVOICE_claim','UX_COLLECTION_NOTE_reference','UX_COLLECTION_NOTE_claim_panel','UX_REMITTANCE_REF_collection_note') AND is_unique=1 AND is_disabled=0)<>4 THROW 51119,'Uniqueness guards missing.',1;
SELECT @@SERVERNAME ServerName,DB_NAME() DatabaseName,N'Claim invoicing schema verified' Result;
SQL
echo 'Schema verified. Deploy backend then frontend; check /health/live and /health/ready before the controlled claim test.'
'@
$claimDeployment.Replace("`r`n", "`n") | wsl.exe -d $Distribution -- bash -s
if ($LASTEXITCODE -ne 0) { throw 'Claim invoicing deployment failed. Keep application services stopped; inspect the SQL error before deploying images.' }
