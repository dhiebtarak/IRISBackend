# Session expiry, frontend updates and FCFA deployment

Run these commands **on the existing hosted WSL2 Ubuntu installation**, not the development PC. Paths and service names come from `deploy/compose.yaml` and the existing deployment handoff. No hosted command was executed during development. Review the approved changes before running them.

## 1. Confirm testing branches and pull approved revisions

```bash
set -euo pipefail
set +x
cd /home/iris-machine/optima-rea
for repo in FrontAuthIRIS IRISBackend; do
  test "$(git -C "$repo" branch --show-current)" = testing
  test -z "$(git -C "$repo" status --porcelain)" || {
    printf 'Uncommitted work in %s; preserve it and stop.\n' "$repo" >&2
    exit 1
  }
done
# Only run after the reviewed revisions are available on the approved remote.
git -C FrontAuthIRIS pull --ff-only origin testing
git -C IRISBackend pull --ff-only origin testing
cd IRISBackend/deploy
docker compose ps
```

Do not print `.env` or run `docker compose config` into logs: it expands secrets. The database service is `db`; its image includes `/opt/mssql-tools18/bin/sqlcmd`. `DB_NAME` is configured as `IRISBackend` in `.env.example`. The scripts below stop if the actual configured name differs. SA is used for backup/reference-data administration; the runtime `DB_USER` is restricted to application reads/writes. Credentials remain in the existing protected container environment, passed through `SQLCMDPASSWORD`, never command-line `-P` values.

## 2. Verify the actual hosted schema and identity

The supplied `FrontAuthIRIS/01-schema.sql` was checked: the eight column names below match that export. The running hosted database was not accessed during development. This mandatory preflight verifies its actual columns before executing the data change.

```bash
docker compose exec -T db /bin/bash -lc '
  set -euo pipefail
  set +x
  test "$DB_NAME" = IRISBackend
  export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"
  exec /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -V 16 -d "$DB_NAME"
' <<'SQL'
SELECT @@SERVERNAME AS ServerName, DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'IRISBackend'
    THROW 51000, 'Unexpected target database. Stop.', 1;
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CURRENCY')
    AND name IN (N'id_currency',N'currency_code',N'currency_symbol',N'name',
                 N'decimal_places',N'is_default',N'is_active',N'country')) <> 8
    THROW 51001, 'Currency schema differs from the approved export. Stop.', 1;
SELECT name, TYPE_NAME(system_type_id) AS DataType, max_length, is_identity
FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CURRENCY') ORDER BY column_id;
SELECT id_currency,currency_code,decimal_places,is_default,is_active
FROM dbo.CURRENCY ORDER BY id_currency;
SQL
```

Review that output before continuing. Never run `dotnet ef database update` against the imported hosted schema.

## 3. Create, verify and copy a recoverable database backup

```bash
backup_name="IRISBackend_before_fcfa_$(date -u +%Y%m%dT%H%M%SZ).bak"
docker compose exec -T -e IRIS_BACKUP_FILE="/var/opt/mssql/backups/$backup_name" db /bin/bash -lc '
  set -euo pipefail
  set +x
  test "$DB_NAME" = IRISBackend
  mkdir -p /var/opt/mssql/backups
  export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"
  exec /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -V 16 -d master -v BackupFile="$IRIS_BACKUP_FILE"
' <<'SQL'
BACKUP DATABASE [IRISBackend] TO DISK=N'$(BackupFile)' WITH COPY_ONLY,CHECKSUM;
RESTORE VERIFYONLY FROM DISK=N'$(BackupFile)' WITH CHECKSUM;
SELECT N'$(BackupFile)' AS VerifiedBackup;
SQL
mkdir -p backups
docker compose cp "db:/var/opt/mssql/backups/$backup_name" "./backups/$backup_name"
test -s "./backups/$backup_name"
```

Stop on any backup, verification or copy failure. Keep this backup and the prior approved frontend/backend revisions available for recovery.

## 4. Stream the approved SQL into the current database container

The file is streamed on standard input, so no guessed container mount is needed. SQLCMD `-b -V 16` and Bash `set -e` propagate SQL errors as nonzero exit codes. The script itself uses `XACT_ABORT`, a transaction and rollback on errors.

```bash
test -f ../IRISBackend/Deployment/20261116_add_fcfa_currency.sql
docker compose exec -T db /bin/bash -lc '
  set -euo pipefail
  set +x
  test "$DB_NAME" = IRISBackend
  export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"
  exec /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -V 16 -f 65001 -d "$DB_NAME"
' < ../IRISBackend/Deployment/20261116_add_fcfa_currency.sql

docker compose exec -T db /bin/bash -lc '
  set -euo pipefail
  set +x
  test "$DB_NAME" = IRISBackend
  export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"
  exec /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -V 16 -d "$DB_NAME"
' <<'SQL'
SELECT id_currency,currency_code,currency_symbol,name,decimal_places,is_default,is_active,country
FROM dbo.CURRENCY WHERE currency_code=N'FCFA';
IF (SELECT COUNT(*) FROM dbo.CURRENCY WHERE UPPER(LTRIM(RTRIM(currency_code)))=N'FCFA') <> 1
    THROW 51002, 'Expected one FCFA record.', 1;
SQL
```

Expected new record: `FCFA / FCFA / Franc CFA / 0 / is_default=0 / is_active=1 / West and Central Africa`. No IDs are specified or reseeded. Existing non-FCFA currencies are untouched. For an already existing FCFA record, the supplied requirement deliberately preserves its configured `is_default`; review an existing true value rather than silently changing a hosted default. Multiple normalized FCFA records abort the operation.

New installations still import the original nine exported currencies and preserve their IDs, then `db/import-reference-data.sh` invokes this same canonical supplemental script. Do not alter the original UTF-16 export or rerun the initial-import script against a populated database.

## 5. Rebuild and redeploy

```bash
export FRONTEND_BUILD_REVISION="$(git -C ../../FrontAuthIRIS rev-parse HEAD)"
docker compose build api web
docker compose up -d --force-recreate api web
docker compose ps
curl --fail --show-error http://localhost:8080/health/live
curl --fail --show-error http://localhost:8080/health/ready
curl --fail --show-error http://localhost:8080/version.json
curl --fail --show-error -I http://localhost:8080/index.html
curl --fail --show-error -I http://localhost:8080/version.json
```

`index.html` and `version.json` must return `Cache-Control: no-store, no-cache, must-revalidate`. Hashed root bundles use `public, max-age=31536000, immutable`. Other routes retain existing caching behavior. Angular production builds must use `npm run build`, which embeds one revision/timestamp ID and writes the same ID to the built `version.json`; Docker already uses that command. Direct `ng serve` intentionally has no deployment-update polling.

## 6. Manual validation

- Log in successfully. Invalid credentials must remain a normal login error. Check authorized workspaces and ensure a 403 does not show session expiry.
- In an isolated test deployment with a short configured `Jwt:ExpirationMinutes`, let the session expire without API activity. Exactly one blocking popup must show `Your session has reached its limit. Re-login to continue.` and one blue `Log out` button.
- Test simultaneous requests at expiry, keyboard focus/Tab/Shift+Tab, Escape and backdrop clicks. The popup stays above other dialogs and cannot be dismissed. Initial focus is on `Log out`.
- Open two authenticated tabs. Expiration clears credentials across matching sessions; logout redirects protected tabs to `/login`. Browser Back cannot reopen protected content. Also test server logout with the API offline.
- Revoke a session in Administrator Access Activity: the API returns `SESSION_REVOKED`, clears the frontend session and requires login without falsely calling it a time-limit expiry.
- Leave an old **production build** open, redeploy the frontend, and wait up to five minutes, reactivate the tab, or reconnect it. One `A new version is available.` notice should appear.
- Dirty a form, click `Reload latest version`, cancel the confirmation and verify the form remains. Confirm the next attempt and verify the latest `version.json`/hashed bundles load. Session expiry must suppress the update notice and block reload until logout.
- Check FCFA in contract, bank-account and relevant financial selectors. Save a test record using its actual API-supplied ID and verify zero-decimal display. Confirm TND and other original currencies still work.
- Use the browser network panel to verify 401 JSON codes. `SESSION_EXPIRED` is for expired JWT/server sessions, `SESSION_REVOKED` for revocations and `SESSION_UNAVAILABLE` for other known unavailable sessions. Invalid/missing tokens retain ordinary authentication failures. Do not copy tokens into reports.

## Product Owner decision

This implementation intentionally stores **FCFA**, as requested. XOF (West Africa) and XAF (Central Africa) are distinct ISO currencies. Decide how exchange-rate feeds, settlement and reporting should distinguish them before integrating those features; neither ISO currency is silently substituted or inserted here.
