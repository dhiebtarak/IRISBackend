# Administrator Access Activity

## Implementation and existing functionality

The existing `UserLoginAudits`, `LoginAuditRepository`, and `LoginAuditService` recorded successful/failed login attempts with optional user ID, attempted identifier, result, failure reason, timestamp, remote IP and user agent. They remain the single authentication audit store and writer. No second audit controller, repository or table was added.

There was no session entity or refresh-token flow. `AUTH_SESSION` now tracks login, last activity, expiration, explicit logout and administrative revocation. `UserLoginAudits` gains `EventType`, nullable `SessionId`, and nullable `ActorUserId`. Historical successes/failures are backfilled without replacing existing rows. Account enable/disable actions record AccountUnlocked/AccountLocked through the same audit service. The existing password check uses `lockoutOnFailure: false`; this implementation preserves that policy and generic login errors.

The Administrator sidebar contains exactly Create User, Accounts, Permissions and Access Activity. `/workspace/administrator/access-activity` is lazy-loaded and guarded; old `/workspace/admin/users/:action` links redirect to their canonical equivalents. Operational sidebar definitions are unchanged. Administrator module grants and the operational Admin bypass are removed in the frontend and backend module policies. Admin remains an Identity administration role, never a provisioning staff position. Existing POST /api/users provisioning and disabled public registration remain intact.

## Administrator API

All routes below require the existing Identity `Admin` role:

- GET `/api/admin/access-activity/summary`
- GET `/api/admin/access-activity/sessions`
- GET `/api/admin/access-activity/sessions/{sessionId}`
- GET `/api/admin/access-activity/users/{userId}/history`
- POST `/api/admin/access-activity/sessions/{sessionId}/revoke`
- POST `/api/admin/access-activity/users/{userId}/revoke-all`

Revocation bodies contain only `{ "reason": "Session revoked by Administrator" }`; reasons must contain 1-200 printable characters. Session-list and history responses use `items`, `totalCount`, `pageNumber`, `pageSize`. Page sizes are limited to 100 and page numbers to 1,000,000. Filters and sort fields are validated with FluentValidation. List SQL applies filtering, stable sorting and pagination before materialization. It includes failed attempts and account state events without inventing session records for them. Date filters select login/attempt times, or lifecycle event times when filtering Logout, SessionExpired or SessionRevoked. User history returns individual immutable audit events, newest first, with event/result/date filters. Role filters use Admin or the existing officer positions, preserving the staff mapping.

## Lifecycle and security decisions

- Successful authentication creates a 32-byte cryptographically random session identifier, saves a session and its LoginSuccessful audit event atomically, and emits `sid` in the existing JWT response. The token and session share the same expiration instant. JWTs, refresh tokens, password hashes and security stamps are never persisted in session/audit records or returned by these APIs.
- JWT inbound claim mapping is explicitly disabled so `sid` is available consistently; existing full URI Identity role and user ID claims retain their meanings. Tokens predating this release have no `sid` and require a new login after deployment.
- Session metadata uses `Connection.RemoteIpAddress` after the existing trusted forwarded-header middleware. Arbitrary forwarded IP headers are not read. User agents, identifiers and reasons have bounded lengths and control characters removed/validated.
- JWT validation checks session status using `IDistributedCache`, with 15-second positive entries by default. Terminal session markers override positive entries and are published before a revocation transaction commits. Failed marker writes roll back the transaction. In-flight requests already authorized may finish. Account existence, live roles, modules and lockout status continue to be checked on authenticated requests.
- The current registration is `AddDistributedMemoryCache`, appropriate for the current single process. **Before multiple instances are deployed, register a genuinely shared IDistributedCache provider for all instances** so terminal markers propagate immediately. Do not deploy multiple instances with separate memory caches.
- POST `/api/auth/logout` closes only the authenticated user's `sid`, records one Logout event and is idempotent. The authentication callback permits an already-logged-out sid only on this logout action while its JWT remains valid. Browser closure never calls or simulates explicit logout. No refresh-token mechanism exists to invalidate.
- Authenticated API activity is throttled through a five-minute cache entry and a conditional SQL update; static requests and health paths are excluded. Session status is never inferred from browser presence.
- A minute-based worker expires bounded batches, records one SessionExpired event per transition, and applies retention. Serializable SQL transactions make state changes and their audit insert atomic across workers. Competing transactions can be retried on the next maintenance interval after a transient deadlock.
- Session durations derive from login and the relevant end timestamp. UTC timestamp conversions preserve the Z suffix on API responses. The UI formats durations and updates active session displays each minute. Summary day boundaries use UTC; the average includes all retained sessions. Display timestamps and date-input controls use the browser's local timezone.
- Accounts metrics include historical successful audit logins and current session counts. Existing account disablement uses Identity lockout: a long/permanent lockout displays Disabled, a temporary lockout displays Locked.
- Administrative revocation records actor and reason. The UI confirms destructive session actions and warns before revoking the current session. Audit events have no edit API or UI.

## Deployment and rollback

Run `IRISBackend/Deployment/20261103_access_activity.sql` against the configured SQL Server database before deploying this application. No application startup migration was added. The script is transactional, repeatable, preserves audit/Identity data, adds restrictive foreign keys and unique/indexed session identifiers, and indexes user/status/timestamp queries. It was tested twice consecutively in an isolated database, including preservation and classification of historical successful/failed audit rows.

Back up before release. The rollback approach is documented at the end of the script: stop the new binary/worker, restore the old binary, retain the new schema and audit evidence. Do not destructively drop/recreate Identity or audit tables. Only approve any eventual removal after exporting the security evidence.

Nonsecret configuration (defaults shown):

```json
{ "AccessActivity": { "RetentionDays": 365, "ValidationCacheSeconds": 15 } }
```

Retention is validated between 1 and 3650 days; validation cache lifetime between 1 and 30 seconds. Cleanup deletes old audit events in bounded batches, then terminal sessions only when no retained event references them. Configure retention according to your organization's policy before production deployment.

The production database has not been changed by this task. Tests create/delete only isolated GUID-named LocalDB test databases. SQL model changes are deliberately deployed through the separate script rather than automatic migrations.

## Files changed by this continuation

Backend existing files extended:
`Program.cs`, `IRISBackend.csproj`, `Models/IRISBackendContext.Contracts.cs`, `Models/UserLoginAudit.cs`, `DTOs/AdminUserManagementDtos.cs`, `Services/AuthService.cs`, `Services/ITokenService.cs`, `Services/TokenService.cs`, `Services/ILoginAuditService.cs`, `Services/LoginAuditService.cs`, `Services/UserService.cs`, `Services/FinancialAccessRules.cs` (paths relative to IRISBackend/).

Backend new files:
`Controllers/AccessActivityController.cs`, `DTOs/AccessActivityDtos.cs`, `Models/AuthSession.cs`, `Models/IRISBackendContext.AccessActivity.cs`, `Services/AuthSessionService.cs`, `Services/AccessActivityService.cs`, `Validators/AccessActivityQueryValidator.cs`, `Properties/AccessActivityAssemblyInfo.cs`, `Deployment/20261103_access_activity.sql`; and `IRISBackend.Tests/AccessActivityTests.cs`, `AccessActivityLifecycleIntegrationTests.cs`, `AccessActivityHttpAuthorizationTests.cs`.

Frontend existing work extended:
`src/app/app.routes.ts`, `shared/services/auth.ts`, `shared/guards/workspace.guard.ts`, `shared/guards/admin.guard.spec.ts`, `workspaces/reassurance/admin-workspace.component.ts`, `admin-account-management.component.ts`, `shared/role-workspace-shell.component.ts` (abbreviated paths after src/app/).

Frontend new files:
`shared/services/access-activity.service.ts` and `.spec.ts`, `shared/guards/workspace.guard.spec.ts`, `workspaces/reassurance/admin-access-activity.component.ts`, `.html`, `.spec.ts`, and `admin-account-management.component.spec.ts`.

The original uncommitted registration, staff workspace, permission, controller and DTO changes were preserved. No reset, revert, checkout, clean, commit or push was performed.

## Validation

Backend build passed. Focused backend tests passed, including isolated SQL Server login/logout/expiration/revocation/filter/summary tests, repeatable deployment and audit backfill, cached revocation rejection, HTTP 403/401 enforcement, provisioning and public-registration checks. Angular production build passed (existing 900 kB initial-bundle warning remains). Focused Angular tests exercise the four-link Admin sidebar, route isolation, backend loading, debounce, filters/paging, badges, durations, confirmation, loading/empty/error states and Accounts navigation. Final focused results: 42 backend tests passed; 16 frontend tests across six files passed. Backend build completed with zero warnings/errors; the test project retains an existing nullable warning in OutgoingRemittanceIntegrationTests.cs. TypeScript spec compilation also passed.

The broader backend run was stopped after unrelated contract/document/financial tests failed inside the sandbox. Two ContractReadServiceTests overview cases were rerun and still fail because they expect an empty financial-note URL while the current service returns a URL. That service and those assertions were not changed by this continuation; their behavior remains unresolved. Full-suite success is not claimed.
