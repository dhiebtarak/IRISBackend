# Session expiration, deployed-version notices and FCFA completion report

Implemented on 2026-11-16 across the existing FrontAuthIRIS and IRISBackend checkouts. Both were confirmed readable/writable and Git-clean before edits. No reset, revert, clean, checkout, deletion of repository content, commit, merge or push was performed. Only the verified local Development database was changed; no remote/office database was contacted.

## Previous behavior and resulting behavior

The backend already created AUTH_SESSION rows, embedded `sid`/`exp` in JWTs, validated sessions and recorded expiration/revocation/logout audit events. Its bearer challenge could not distinguish session expiration from other 401 failures. JWT validation also allowed one minute of clock skew. The frontend interceptor merely attached the stored token; guards considered token presence sufficient and there was no expiration timer or blocking dialog.

The existing Auth service now owns expiration timing, authenticated-request cancellation, storage cleanup and cross-tab signaling. JWT `exp` controls the deadline; the successful-login `serverTimeUtc` corrects local clock differences. Startup, successful login/token replacement, focus and tab activation recheck expiration. The frontend has no separate session-duration constant. Backend JWT clock skew is zero; existing server session validation/cache/audit lifecycle is preserved.

Expiration clears authentication data and opens one root-level, focus-trapped dialog with the exact required message and one blue `Log out` button. The content behind it is inert. Backdrop/Escape cannot dismiss it, it is hidden on login, and update notices are suppressed while the session is expired. Concurrent requests cannot create duplicate dialogs. Existing authenticated requests are canceled where possible. Invalid login, generic 401, 403, validation/network errors and public/external requests do not trigger this time-limit dialog.

Logout starts local cleanup and replacement navigation to `/login` immediately; a best-effort request still uses the captured bearer token. A three-second network timeout or 401 cannot prevent local logout. The logout endpoint and its idempotent state transitions are unchanged. Guards prevent protected Back navigation and allow logout to leave dirty forms without an unrelated navigation confirmation.

## API contract and multiple tabs

Confirmed expiration returns HTTP 401 with `code: SESSION_EXPIRED` and `message: Your session has reached its limit. Re-login to continue.`. `SESSION_REVOKED` and `SESSION_UNAVAILABLE` remain distinct session failures; they clear the session and redirect without falsely claiming a time-limit expiry. Missing/invalid tokens retain ordinary authentication failures; invalid login retains its existing 401 response without an expiration code; forbidden access remains 403.

BroadcastChannel `iris-auth` synchronizes expiration/logout, with a storage-event fallback on `iris_auth_event`. Messages contain an event type, session identifier and event ID, never a token. Stale messages for a previous signed-in session are ignored. Local/session storage keys under the application's `iris_auth_` namespace are cleared, covering the token, optional refresh/session keys, cached user, roles and modules. Application settings outside that namespace are preserved.

## Version detection and caching

No Angular Service Worker, PWA configuration or existing update service was present. The lightweight build-version mechanism therefore introduces no second updater. `npm run build` embeds a Git/CI revision plus build timestamp using Angular's build define and writes the identical identifier to `dist/FrontAuthIRIS/browser/version.json`. Docker accepts the revision through its build argument; the hosted guide supplies the checked-out frontend SHA.

The single root update service checks after startup, every five minutes, on tab activation and on reconnect. Requests bypass authentication and use cache-busting/no-cache headers. In-flight and repeated version checks are deduplicated; errors do not affect the application, and listeners/timers/subscriptions are cleaned up. If a deployment rolls back to the running version, its stale update notice is removed. Development `ng serve` intentionally does not poll for deployment updates.

Reload is explicit. Dirty Angular forms require confirmation; session expiration is rechecked after confirmation. A compatible, unexpired session can remain signed in; an incompatible manifest authentication schema clears authentication and loads `/login`. No reload loop is scheduled. A compiled-bundle check confirmed that the production manifest identifier occurs inside the running application bundle.

Nginx now serves `index.html` and `version.json` with `no-store, no-cache, must-revalidate`, while root hashed JavaScript/CSS bundles use one-year immutable caching. API/static legacy proxy behavior is unchanged. Docker/Nginx are not installed on the development command path; actual hosted response headers/redeployment are operator checks, not claimed as executed.

## FCFA and database verification

`Deployment/20261116_add_fcfa_currency.sql` performs a transactional, repeatable reference-data upsert, locking the normalized code range to avoid concurrent duplicate insertion. It rejects multiple normalized FCFA rows, never specifies/reseeds an identity, never changes other currencies, and preserves a pre-existing FCFA default flag as explicitly required. A newly inserted FCFA is non-default. There is no schema redesign or EF migration.

The supplied hosted schema export and the live local table were both inspected: `id_currency`, `currency_code`, `currency_symbol`, `name`, `decimal_places`, `is_default`, `is_active`, `country`. Hosted runtime schema is intentionally unverified until the mandatory preflight in the deployment guide is executed.

Local target: Development, `(localdb)\MSSQLLocalDB`, database `IRISBackend`; actual server during this run: `IRISTNLT40\LOCALDB#F9B1139C`. Local-machine/LocalDB/database/file checks rejected any other identity before writes. Backup and `RESTORE VERIFYONLY ... WITH CHECKSUM` completed successfully:

`artifacts/database-backups/IRISBackend_before_fcfa_20261116_135706_318.bak`

The script was executed twice. FCFA exists once as local ID **10**, symbol **FCFA**, name **Franc CFA**, precision **0**, active **true**, default **false**, country **West and Central Africa**. Hashes of all other currency rows, including their IDs, matched before/after. Financial tables were not updated by the script.

All existing active-currency selectors use the currency API; no production frontend currency array was added. The canonical new-installation reference importer preserves the original nine currencies/IDs, then invokes this same supplemental script. XOF/XAF were not inserted or substituted. The Product Owner still needs to decide how future exchange-rate feeds, settlement and reporting distinguish West African XOF from Central African XAF while the requested stored code remains FCFA.

## Changed files by area

Frontend (paths relative to FrontAuthIRIS):

- `src/app/shared/services/auth.ts`, `src/app/shared/interceptors/auth.interceptor.ts`: session timing, logout cleanup, cancellation, response classification and tab synchronization.
- `src/app/shared/guards/{auth,admin,workspace,pending-changes}.guard.ts`: expiration-aware protected navigation and logout handling.
- `src/app/shared/services/app-update.service.ts`, `src/app/shared/components/application-notices.component.ts`: version polling and shared notices.
- `src/app/app.component.{ts,html}`: one global notices instance and inert protected content.
- `src/app/user/user.component.ts`, `src/app/workspaces/reassurance/shared/role-workspace-shell.component.ts`: replacement logout navigation.
- `scripts/build.mjs`, `src/build-version.d.ts`, `package.json`, `Dockerfile`, `deploy/nginx.conf`: build manifest, revision propagation and selective cache headers.

Backend (paths relative to IRISBackend checkout):

- `IRISBackend/Services/AuthSessionService.cs`, `IRISBackend/Services/SessionAuthenticationResponses.cs`, `IRISBackend/Program.cs`: explicit validation failure codes and consistent JWT challenges.
- `IRISBackend/DTOs/AuthResponseDto.cs`: authoritative server UTC time in login responses.
- `IRISBackend/Deployment/20261116_add_fcfa_currency.sql`, `IRISBackend/IRISBackend.csproj`: repeatable FCFA script and output packaging.
- `deploy/compose.yaml`, `deploy/db/import-reference-data.sh`, `deploy/README.md`, `deploy/session-update-fcfa-20261116.md`: frontend revision argument, canonical reference additions and exact hosted commands.

Tests:

- Frontend: new `auth.session.spec.ts`, `app-update.service.spec.ts`, `application-notices.component.spec.ts`, `session-routing.spec.ts`; updated `facultative-information.component.spec.ts` with API-driven FCFA rendering/submission and zero-precision coverage.
- Backend: new `CurrencyDeploymentIntegrationTests.cs`; updated `AccessActivityTests.cs`, `AccessActivityHttpAuthorizationTests.cs`, `AccessActivityLifecycleIntegrationTests.cs`, and the test-only authentication fixture in `FinanceWorkspaceImprovementsTests.cs` to recognize the Claims module.

## Validation and known baseline failures

- Production Angular build passed; existing initial-bundle warning remains (~1.06 MB against a 900 kB warning budget, below the 1.2 MB error budget).
- Backend solution build passed with zero warnings/errors.
- Focused frontend set: **66 tests passed across eight files**. Coverage includes timing, already-expired/malformed tokens, server clock offset, concurrent 401s, ordinary/public errors, failed/offline logout, BroadcastChannel/fallback synchronization, routing, focus/non-dismissal, update priority/reload/dirty forms/polling cleanup, and FCFA submission.
- Focused backend set: **18 tests passed**. Includes actual bearer HTTP challenges, session lifecycle and audit idempotence, invalid login, reference-data API protection, FCFA preservation/repeatability, existing-default preservation and duplicate-row rejection. SQL integration tests used newly named isolated LocalDB test databases.
- Shell syntax checks passed for the reference importer and every Bash command block in the hosted guide. `git diff --check` passed in both repositories.
- A broader frontend run reported 81 passing tests, two failing layout tests and two unhandled ContractsService assertion errors. `front-officer-layout.component.spec.ts` mocks Auth without the pre-existing `isAdmin` method. `contracts.service.spec.ts:327` and `:426` expect older DTO shapes. These fixtures and the ContractsService implementation are unchanged from HEAD; they are recorded as baseline failures and are not hidden by claiming a full-suite pass.

The running local backend process was verified before stopping it to release its DLL lock. It is left stopped for Visual Studio to restart with the new build. No hosted Docker deploy, real-browser office login/expiration exercise or hosted cache-header test was performed here.

## Exact hosted commands and manual checklist

Follow [the hosted execution guide](../deploy/session-update-fcfa-20261116.md). It contains the concrete `testing` branch checks, approved pulls, environment-backed SQLCMD commands for service `db`, verified backup/copy, schema preflight, script execution/verification, `api`/`web` rebuild and health checks. Its final section is the manual validation checklist, including two tabs, offline logout, a dirty form, a newer deployed build, session-priority behavior, FCFA selection and the XOF/XAF decision. Do not execute EF migrations on the imported office database.
