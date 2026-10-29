#!/usr/bin/env bash
set -Eeuo pipefail

admin_id="${1:?Usage: bash import-reference-data.sh <bootstrapped-admin-user-id>}"
if [[ ! "$admin_id" =~ ^[0-9A-Fa-f-]{36}$ ]]; then
  echo "Supply the actual Identity user ID returned by the deployment database (expected GUID format)." >&2
  exit 2
fi

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
deploy_dir="$(cd -- "$script_dir/.." && pwd)"
workspace_dir="$(cd -- "$script_dir/../../.." && pwd)"
reference_file="$workspace_dir/FrontAuthIRIS/02-reference-data.sql"
if [[ ! -f "$reference_file" ]]; then
  echo "Reference-data export not found: $reference_file" >&2
  exit 2
fi

compose() {
  docker compose --project-directory "$deploy_dir" -f "$deploy_dir/compose.yaml" "$@"
}

temp_dir="$(mktemp -d)"
trap 'rm -rf "$temp_dir"' EXIT
iconv -f UTF-16 -t UTF-8 "$reference_file" > "$temp_dir/reference-utf8.sql"

for pair in 'CURRENCY:9' 'COVERAGE_ITEM:38' 'COVERAGE_RELATION:37'; do
  table="${pair%%:*}"
  expected="${pair##*:}"
  actual="$(grep -Eic "^INSERT \[dbo\]\.\[$table\]" "$temp_dir/reference-utf8.sql" || true)"
  if [[ "$actual" != "$expected" ]]; then
    echo "Unexpected $table export row count: $actual (expected $expected). No import performed." >&2
    exit 4
  fi
done

mapfile -t audit_ids < <(grep -Eoih '[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}' "$temp_dir/reference-utf8.sql" | sort -fu)
if [[ "${#audit_ids[@]}" -ne 1 ]]; then
  echo "Expected one source audit-user ID in the export; found ${#audit_ids[@]}. Review the source before importing." >&2
  exit 4
fi
source_audit_id="${audit_ids[0]}"
sed "s/${source_audit_id}/${admin_id}/gI" "$temp_dir/reference-utf8.sql" > "$temp_dir/reference-remapped.sql"
if grep -Eqi "$source_audit_id" "$temp_dir/reference-remapped.sql"; then
  echo "Source development audit ID remains in the prepared import; refusing to continue." >&2
  exit 4
fi

{
  printf '%s\n' ':On Error exit' 'SET NOCOUNT ON;' 'SET XACT_ABORT ON;'
  cat <<SQL
IF NOT EXISTS (
    SELECT 1
    FROM dbo.AspNetUsers AS u
    INNER JOIN dbo.AspNetUserRoles AS ur ON ur.UserId = u.Id
    INNER JOIN dbo.AspNetRoles AS r ON r.Id = ur.RoleId
    WHERE u.Id = N'$admin_id' AND r.Name = N'Admin'
)
    THROW 51020, 'The supplied user does not exist or is not assigned to the Admin role.', 1;
IF OBJECT_ID(N'dbo.CURRENCY', N'U') IS NULL OR
   OBJECT_ID(N'dbo.COVERAGE_ITEM', N'U') IS NULL OR
   OBJECT_ID(N'dbo.COVERAGE_RELATION', N'U') IS NULL
    THROW 51021, 'Required reference-data tables are missing.', 1;
IF EXISTS (SELECT 1 FROM dbo.CURRENCY) OR
   EXISTS (SELECT 1 FROM dbo.COVERAGE_ITEM) OR
   EXISTS (SELECT 1 FROM dbo.COVERAGE_RELATION)
    THROW 51022, 'Reference-data target tables are not empty.', 1;
IF EXISTS (SELECT 1 FROM dbo.SLIP_TEMPLATE) OR
   EXISTS (SELECT 1 FROM dbo.RISK_NATURE) OR
   EXISTS (SELECT 1 FROM dbo.SUB_RISK)
    THROW 51023, 'A table expected to remain empty already contains data.', 1;
IF EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory)
    THROW 51024, 'EF migration history must remain empty for this imported-schema deployment.', 1;
DECLARE @tableName nvarchar(517);
DECLARE @checkTableSql nvarchar(max);
DECLARE unexpected_tables CURSOR LOCAL FAST_FORWARD FOR
    SELECT QUOTENAME(s.name) + N'.' + QUOTENAME(t.name)
    FROM sys.tables AS t
    INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
    WHERE t.is_ms_shipped = 0
      AND NOT (s.name = N'dbo' AND t.name IN (
          N'AspNetUsers', N'AspNetRoles', N'AspNetUserRoles',
          N'CURRENCY', N'COVERAGE_ITEM', N'COVERAGE_RELATION',
          N'__EFMigrationsHistory', N'SLIP_TEMPLATE', N'RISK_NATURE', N'SUB_RISK'
      ));
OPEN unexpected_tables;
FETCH NEXT FROM unexpected_tables INTO @tableName;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @checkTableSql = N'IF EXISTS (SELECT 1 FROM ' + @tableName + N') '
        + N'THROW 51030, ''Unexpected non-empty table found before reference-data import.'', 1;';
    EXEC sys.sp_executesql @checkTableSql;
    FETCH NEXT FROM unexpected_tables INTO @tableName;
END;
CLOSE unexpected_tables;
DEALLOCATE unexpected_tables;
BEGIN TRANSACTION;
GO
SQL
  cat "$temp_dir/reference-remapped.sql"
  cat <<'SQL'
IF (SELECT COUNT_BIG(*) FROM dbo.CURRENCY) <> 9
    THROW 51025, 'Expected exactly 9 imported currencies.', 1;
IF (SELECT COUNT_BIG(*) FROM dbo.COVERAGE_ITEM) <> 38
    THROW 51026, 'Expected exactly 38 imported coverage items.', 1;
IF (SELECT COUNT_BIG(*) FROM dbo.COVERAGE_RELATION) <> 37
    THROW 51027, 'Expected exactly 37 imported coverage relationships.', 1;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE is_disabled = 1 OR is_not_trusted = 1)
    THROW 51028, 'At least one foreign key is disabled or untrusted.', 1;
IF EXISTS (SELECT 1 FROM dbo.SLIP_TEMPLATE) OR
   EXISTS (SELECT 1 FROM dbo.RISK_NATURE) OR
   EXISTS (SELECT 1 FROM dbo.SUB_RISK)
    THROW 51029, 'Tables expected to be empty were unexpectedly populated.', 1;
COMMIT TRANSACTION;
GO
SQL
} > "$temp_dir/import.sql"

compose exec -T db sh -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -V 16 -f 65001 -d "$DB_NAME"' \
  < "$temp_dir/import.sql"

echo "Imported 9 currencies, 38 coverage items and 37 coverage relationships. Audit user references now point to the supplied deployed Admin ID."
