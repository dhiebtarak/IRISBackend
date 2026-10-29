#!/usr/bin/env bash
set -Eeuo pipefail

if [[ $# -ne 0 ]]; then
  echo "Usage: bash import-schema.sh (target database comes from deploy/.env DB_NAME)" >&2
  exit 2
fi

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
deploy_dir="$(cd -- "$script_dir/.." && pwd)"
workspace_dir="$(cd -- "$script_dir/../../.." && pwd)"
schema_file="$workspace_dir/FrontAuthIRIS/01-schema.sql"
if [[ ! -f "$schema_file" ]]; then
  echo "Schema export not found: $schema_file" >&2
  exit 2
fi

compose() {
  docker compose --project-directory "$deploy_dir" -f "$deploy_dir/compose.yaml" "$@"
}

database_name="$(compose exec -T db sh -c 'printf %s "$DB_NAME"' | tr -d '\r\n')"
if [[ ! "$database_name" =~ ^[A-Za-z0-9_]+$ ]]; then
  echo "The db container has an invalid or empty DB_NAME; check deploy/.env." >&2
  exit 2
fi

temp_dir="$(mktemp -d)"
trap 'rm -rf "$temp_dir"' EXIT
iconv -f UTF-16 -t UTF-8 "$schema_file" > "$temp_dir/schema-utf8.sql"

table_count="$(compose exec -T db sh -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -f 65001 -d "$DB_NAME" -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT_BIG(*) FROM sys.tables WHERE is_ms_shipped = 0"' | tr -d '[:space:]')"
if [[ "$table_count" != "0" ]]; then
  echo "Refusing schema import: target database has $table_count user tables; expected an empty database." >&2
  exit 3
fi

{
  printf '%s\n' ':On Error exit' 'SET XACT_ABORT ON;' 'BEGIN TRANSACTION;' 'GO'
  cat "$temp_dir/schema-utf8.sql"
  cat <<'SQL'
IF (SELECT COUNT_BIG(*) FROM sys.tables WHERE is_ms_shipped = 0) <> 49
    THROW 51010, 'Schema import did not create the expected 49 user tables.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
    THROW 51011, 'EF migrations history table is missing from the schema export.', 1;
IF EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory)
    THROW 51012, 'Schema import unexpectedly populated EF migration history.', 1;
COMMIT TRANSACTION;
GO
SQL
} > "$temp_dir/import.sql"

compose exec -T db sh -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -V 16 -f 65001 -d "$DB_NAME"' \
  < "$temp_dir/import.sql"

echo "Imported the supplied schema into the empty database. No EF migrations were applied and __EFMigrationsHistory remains empty."
