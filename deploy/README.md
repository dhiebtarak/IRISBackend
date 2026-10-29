# Optima-rea deployment handoff

Target host: Windows 11 `DESKTOP-32FH4OL`, office IP `192.168.10.2`, with Docker Compose in WSL2 Ubuntu 24.04. Expected sibling checkouts:

```text
/home/iris-machine/optima-rea/
  FrontAuthIRIS/
  IRISBackend/
```

This is a reviewed deployment setup, not a request to install infrastructure on the development PC. No startup migration runs in the API. Do not run `dotnet ef database update` on the imported database: the repository migration history does not establish the exported business schema as its baseline.

## Why first deployment imports the schema export

The supplied UTF-16LE `FrontAuthIRIS/01-schema.sql` creates 49 tables, including the Identity and business schema, and contains no inserts. The initial EF migration creates Identity tables; later migrations are incremental. The migrations plus `Database/CreateCoveragePrerequisites.sql` do not recreate all tables in the export. The current migration snapshot also does not map these exported tables: `__EFMigrationsHistory`, `CONTRACT_SUB_RISK`, `INVOICE_PANEL_BROKERAGE_RECONCILIATION`, `RISK_NATURE`, and `SUB_RISK`.

The export itself creates `__EFMigrationsHistory` empty. Import it into an empty database, leave that history empty, and do not replay migrations. `CreateCoveragePrerequisites.sql` describes four tables also present in the export, but its `CONTRACT_COVERAGE_SELECTION` definition lacks the export's `is_primary` column. Do not run that prerequisite script for this full-schema deployment. The three reference tables are loaded separately by the guarded import script. `SLIP_TEMPLATE`, `RISK_NATURE`, and `SUB_RISK` remain present and empty.

The reference export has UTF-16LE BOM and contains 9 currencies, 38 coverage items, and 37 coverage relationships. Its audit fields refer to one source development user. `db/import-reference-data.sh` preserves the original file and remaps that one audit ID to the actual deployed Admin ID passed by the operator. The script aborts on unexpected source counts, a nonempty target, a missing user/FK, or mismatched post-import counts.

## Local review changes

Angular uses same-origin `/api`. `FrontAuthIRIS/proxy.conf.json` is development-only and forwards `ng serve` requests to `http://localhost:5151`. Production API traffic is proxied by Nginx. Compose build contexts resolve the sibling repositories from this file’s location: frontend `../../FrontAuthIRIS`, backend `..` with Dockerfile `IRISBackend/Dockerfile`.

Only Nginx publishes a host port (`8080:80`). The API listens internally on 8080 and is not host-published; SQL Server 1433 is also internal. The API and Nginx images contain their own health-check commands. Nginx proxies `/health/live` and `/health/ready` to the API. The SQL Server image health check uses its included `/opt/mssql-tools18/bin/sqlcmd`.

These changes are currently uncommitted and unpushed. A remote clone alone will not contain them. Transfer both complete reviewed working trees, including the user-supplied exports, or later transfer approved commits.

## Hosting PC - Windows PowerShell

Run elevated on `DESKTOP-32FH4OL`, not on the development PC:

```powershell
Get-NetIPAddress -AddressFamily IPv4 | Where-Object IPAddress -eq '192.168.10.2'
wsl.exe --list --verbose
wsl.exe -d Ubuntu-24.04 -- bash -lc 'hostname; docker version; docker compose version'
Get-NetFirewallHyperVVMCreator
```

Allow only office subnet `192.168.10.0/24` to reach Nginx TCP 8080 through both Windows Firewall and the WSL Hyper-V firewall:

```powershell
New-NetFirewallRule -DisplayName 'Optima-rea office web 8080' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 8080 -RemoteAddress '192.168.10.0/24'

New-NetFirewallHyperVRule -Name 'OptimaReaOfficeWeb8080' -DisplayName 'Optima-rea office web 8080' -Direction Inbound -VMCreatorId '{40E0AC32-46A5-438A-A0B2-2B479E8F2E90}' -Protocol TCP -LocalPorts 8080 -RemoteAddresses '192.168.10.0/24' -Action Allow
```

The Hyper-V rule uses Microsoft’s WSL VM creator ID; verify `Get-NetFirewallHyperVVMCreator` lists WSL before applying. Do not create firewall rules for API or SQL ports. Keep the existing WSL startup task.

## Hosting PC - Ubuntu / WSL2

### Transfer and configure

After transferring the reviewed repositories and originals, confirm both branches/commits and the exports:

```bash
cd /home/iris-machine/optima-rea
git -C FrontAuthIRIS status --short --branch
git -C IRISBackend status --short --branch
test -f FrontAuthIRIS/01-schema.sql
test -f FrontAuthIRIS/02-reference-data.sql
```

Configure deployment secrets on the hosting PC only:

```bash
cd /home/iris-machine/optima-rea/IRISBackend/deploy
cp .env.example .env
chmod 600 .env
editor .env
```

Set the SQL SA/runtime credentials, JWT issuer/audience/signing key, and all three approved IRIS bank accounts. Do not transfer development User Secrets or commit a populated `.env`. Generate strong SQL passwords using Base64URL characters to avoid SQLCMD quoting and semicolon-delimited connection-string issues. The bootstrap Admin variables are one-shot and can be removed after bootstrap. Prefer a managed secret source for normal operation.

Compose explicitly sets `Registration__AllowPublicRegistration=false` for office testing. If the setting is omitted, public registration remains enabled in Development and disabled outside Development. The one-shot bootstrap creates the initial Admin independently of the registration endpoint. This repository has no Admin user-management endpoint; authorized tester accounts must be provisioned by trusted Identity administration tooling using `UserManager` and explicit role assignment before office testing. Do not insert password hashes directly with SQL or temporarily enable public self-registration.

The sample SQL Server image is `mcr.microsoft.com/mssql/server:2025-latest`, which defaults to Developer edition. Verify x86-64 architecture, compatibility and licensing; configure an appropriately licensed production edition through `MSSQL_PID`.

### Build and initialize the empty database

```bash
cd /home/iris-machine/optima-rea/IRISBackend/deploy
docker compose build api web
docker compose up -d db
docker compose ps
```

Provision the restricted runtime login using the SA initialization credential. This creates the named database and grants the API login only `db_datareader`/`db_datawriter`; schema import remains a separate privileged operation:

```bash
docker compose exec -T db sh -c \
  'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -d master -v DB_NAME="$DB_NAME" DB_USER="$DB_USER" DB_PASSWORD="$DB_PASSWORD" -i /opt/iris/provision-runtime-login.sql'
```

Import the full schema into that empty database. This script refuses any existing user tables and uses SQLCMD against the schema decoded from UTF-16:

```bash
bash ./db/import-schema.sh
```

The importer reads the target from the running `db` container's Compose-provided `DB_NAME` and rejects positional arguments, so it cannot target a database different from the one provisioned above. The import validates 49 user tables and an empty `__EFMigrationsHistory`. It never invokes EF migrations.

### Bootstrap Admin and import reference data

Run the one-shot ASP.NET Identity bootstrap only after schema import:

```bash
docker compose run --rm bootstrap-admin
```

Retrieve the actual Admin user ID from the target database (if multiple Admins exist, select the intended bootstrap account):

```bash
docker compose exec -T db sh -c \
  'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -d "$DB_NAME" -Q "SET NOCOUNT ON; SELECT u.Id, u.Email FROM dbo.AspNetUsers u JOIN dbo.AspNetUserRoles ur ON ur.UserId=u.Id JOIN dbo.AspNetRoles r ON r.Id=ur.RoleId WHERE r.Name=CHAR(65)+CHAR(100)+CHAR(109)+CHAR(105)+CHAR(110)"'
```

Import the reference rows with that real ID:

```bash
bash ./db/import-reference-data.sh '<actual-AspNetUsers.Id>'
```

The importer checks the supplied user exists and belongs to the `Admin` role, verifies all three reference targets and expected-empty tables are empty, rejects unexpected populated business tables, preserves source identity values for currency/coverage records, and runs inserts and count/FK checks in one transaction. The reference script replaces the source audit GUID only in a temporary UTF-8 conversion; neither supplied original SQL file is edited. Migration history remains empty.

The approved currency and coverage records in `02-reference-data.sql` are intentionally imported. Do not copy development test users, contracts, invoices, receipts, remittances, or other test transactions. The schema export contains no user data. `SLIP_TEMPLATE`, `RISK_NATURE`, and `SUB_RISK` remain empty; upload slip templates separately if that workflow is needed.

### Start services

```bash
docker compose up -d api web
docker compose ps
docker compose logs --tail=100 api web db
curl -fsS http://127.0.0.1:8080/health/live
curl -fsS http://127.0.0.1:8080/health/ready
```

Office URL: `http://192.168.10.2:8080`. The API’s configured SQL host is `db`; only Nginx has a published port. SQL files, uploads, documents and Data Protection keys persist in Docker volumes. Back up the SQL and file volumes together.

### Future schema changes

Do not run the existing EF migration chain against this imported schema: history is intentionally empty, and the chain begins by creating tables already present. No baseline-history rows have been fabricated. Future schema evolution is not yet cleared for automated EF deployment; first produce and review a real baseline/migration strategy against the exported schema and current model, including the five exported tables absent from the model snapshot.

## Another office PC - acceptance tests

Open `http://192.168.10.2:8080` from a separate office PC. Confirm browser requests use same-origin `/api`, deep links survive refresh, login/contract access and document uploads/downloads work, and uploaded files survive a container restart. Verify both health routes through Nginx and confirm API/SQL ports are not reachable from the office network. Reboot Windows to verify the WSL task and Compose restart policies.

This HTTP setup is for a restricted office-network test only; JWTs are stored in browser local storage and HTTP does not protect them in transit. Use TLS termination at Nginx before production, then enable API HTTPS redirection and retain the configured trusted forwarded proxy.
