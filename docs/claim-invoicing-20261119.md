# Claim-to-invoicing implementation and deployment — 19 November 2026

## Preserved initial workspace changes

Both repository roots were verified readable and writable before edits, and Git status was run separately. The initial uncommitted changes were:

- Frontend: `src/app/contracts/contract-overview/contract-overview.component.ts`, `.html`, `.spec.ts`.
- Backend: `IRISBackend/Services/FinancialWorkflowService.cs`, `PremiumDebitNoteAllocationCalculator.cs`, `IRISBackend.Tests/OutgoingRemittanceIntegrationTests.cs`, `PremiumDebitNoteAllocationCalculatorTests.cs`.
- Backend untracked: `docs/avoir-amount-20261118.md`.

Those existing Avoir amount-limit changes were preserved. No repository history operations were performed.

## Findings and final business rules

The existing shared Claims component, Claims controller and FinancialWorkflowService partial already supported claim entry, attachments, an aggregate collection PDF and two legacy processing modes. Currency and deductible existed in CLAIM; the old form forced contract currency/deductible. Its collection calculation applied deductible and contract share again. Issuance did not create a cedent Avoir. The bell did not handle CLAIM deep links.

The latest request supersedes the earlier `(gross - deductible) × share` formula for new invoicing. **CLAIM.paid_loss is the complete approved settlement**, must be positive, and is never reduced again. Gross loss remains paid loss + expert fees + other fees for display; reserve is separate. A claim's optional deductible is stored independently: null stays null, explicit zero stays zero, decimal(19,4) precision is enforced. Under-deductible is an explicit Front Officer status requiring gross loss <= a provided deductible; it is closed and excluded from SOA. It is not inferred from a Paid Claim's approved settlement.

Currency comes from the existing active-currency API. Contract currency defaults only new entry. Existing issued claims retain their stored amounts, PDF and legacy pairing path. No existing business rows are recalculated or backfilled.

## Files and reuse

Frontend: shared `claims/contract-claims.component.{ts,html,css}`, claim models/service and decimal-text minor-unit utility; existing notification bell; existing contract Avoir history and financial-workspace invoice list. Existing Claims routes/sidebar and module guards are reused. Mr Clippy and queued attachment retries remain intact.

Backend: `FinancialWorkflowService.Claims.cs` and new partial `FinancialWorkflowService.ClaimInvoicing.cs` in the **same service**, ClaimsController/interface/DTOs, claim EF partial/mapping, real Invoice.SourceClaim navigation, remittance collection-note link. Existing AV number allocator, invoice list, Credit Note/Avoir PDF renderer and remittance/netting engine are reused. Claim Avoirs are excluded from premium invoiceability totals and generic premium Avoir pairing, preventing duplicate remittance generation.

SQL: `IRISBackend/Deployment/20261119_claim_invoicing_workflow.sql`. Hosted execution helper: `deploy/Deploy-ClaimInvoicing.ps1`.

PDF: `FinancialWorkflowService.IrisAccountingPdf.cs`, extended with optional Claim metadata. Collection notes use the existing logo, margins, Courier typography, recipient block, financial boxes, amount-in-words and footer; the collection wording differs from the cedent Avoir wording. Both use positive document amounts. Claim documents omit banking details, consistent with the previous Claim collection-note rule. Recipient, claim, currency precision and contract facts are snapshotted at approval; branding/logo still use configured IRIS branding. Long descriptions/recipient information are explicitly abbreviated to fit one page; the full claim remains available in the overview. The required Avoir label is retained on the invoice and rendered in the document.

Tests: claim component/money/notification specs; `ClaimInvoicingIntegrationTests.cs`, `ClaimInvoicingAuthorizationTests.cs`, updated existing claim workflow tests in the shared integration fixture. Earlier unrelated Avoir edits remain preserved.

## API

All routes require authentication. Claims-module access is required for create/edit; BackOffice is required for preview/approve. Read/document access uses the existing Claims-or-BackOffice rule.

- `GET /api/claims`: existing paging plus search, contract/UMR text, cedent text, status, invoicingStatus, currencyId, lossFrom/lossTo, sort and descending. Filtering, ordering and pagination happen in SQL. `contractId`/`soaOnly` remain supported.
- `GET /api/claims/{claimId}`: exact overview, including eligibility, independent currency and approval version.
- Existing context/create/update routes remain. Create/update now accept optional `deductible` and any active `currencyId`.
- `GET /api/claims/{claimId}/invoicing/preview`: authoritative claim, exact paidLossText and eligible panel suggestions.
- `POST /api/claims/{claimId}/invoicing/approve`: only `{ label, version, allocations: [{ panelId, amount }] }`. Decimal amount strings are accepted to avoid a JavaScript precision round trip.
- `GET /api/claims/{claimId}/invoicing`: saved Avoir and collection-note manifest.
- `GET /api/claims/{claimId}/invoicing/documents?kind=bundle`: ZIP of separate PDFs.
- Same URL with `kind=avoir`, or `kind=collection&noteId=...`: individual re-download; note IDs are checked against this claim.
- Existing invoice/Avoir document endpoints delegate Claim Avoirs to the snapshot renderer.
- Existing collection-note issuance route supports retries of previously issued documents; unprocessed claims must use the new approval workflow.

## States, rounding and integrity

Stored claim statuses remain `Outstanding`, `PaidLoss` (displayed **Paid Claim**), `Closed`, `UnderDeductible`. Invoicing state is derived separately: `NotInvoiced` ? `Ready` ? `Invoiced`; `LegacyIssued` identifies pre-existing financial issuance.

A transition into PaidLoss validates financial data and writes one notification per Back Officer recipient in the same serializable transaction. Re-submitting PaidLoss creates none. A genuine later transition back into PaidLoss is a new transition. Notification text includes claim, contract/UMR, cedent, paid loss and currency; the bell opens `/workspace/reassurance/back-officer/claims?claimId={id}` and the shared component fetches that precise overview.

Preview suggests allocations using confirmed, active reinsurer panel signed-share weights normalized to the full paid_loss. Largest remainder assigns residual minor units deterministically by panel ID. Suggestions are editable. Approval requires each eligible panel exactly once, no negative values, currency precision and sum **exactly equal to paid_loss**. No retained portion or hidden deductible/share subtraction. Angular validates with BigInt minor units; SQL-facing calculations use decimal. Zero allocations create zero notes and closed zero-balance remittance records, requiring no payment.

Approval takes a transaction-owned `IRIS:Claim:{id}` application lock and checks a persisted optimistic version. It creates one cedent AVOIR using the existing AV sequence, one note per eligible panel and directly linked Claim remittances, updates approval user/time and reads related notifications in one transaction. Database failures roll everything back. Retrying identical saved label/amount decisions returns existing IDs, even with the old preview version; changed decisions or stale unprocessed versions return 409. PDF generation is read-only and runs after commit. Download retry never approves again.

Schema additions: INVOICE.Claim_ref nullable FK/filtered unique; CLAIM.Invoicing_version, Invoiced_at/by and immutable document-fact JSON; COLLECTION_NOTE.Document_reference nullable/filtered unique; REMITTANCE_REF.Id_collection_note nullable FK/filtered unique. Existing claim/panel uniqueness remains. **COLLECTION_NOTE.Id_claim is canonical**; legacy Claim_ref is preserved and new rows write both equally. No third claim reference, duplicate currency/deductible fields, endorsement substitution, financial-table recreation or automatic EF migration.

## Local database verification

Only the verified `(localdb)\MSSQLLocalDB`, database `IRISBackend`, Development environment was accessed. Runtime local instance at deployment: `IRISTNLT40\LOCALDB#CA1AE35E` (LocalDB's generated instance suffix can change). Two existing claims/two collection notes had matching claim links before deployment.

Backup successfully created and `RESTORE VERIFYONLY ... WITH CHECKSUM` completed:
`artifacts/database-backups/IRISBackend_before_claim_invoicing_20261119_113102_927.bak`.

The additive deployment executed twice successfully. Hashes of every original column in CLAIM, COLLECTION_NOTE, INVOICE, REMITTANCE_REF, AspNetUsers, CURRENCY and RECEIPT_ALLOCATION were unchanged. The verified local backend process was stopped for deployment/build; SQL Server and unrelated applications were not stopped. No hosted database was contacted.

## Exact hosted PowerShell commands (manual only)

These use the actual deployment documented in deploy/README.md: Windows host DESKTOP-32FH4OL, WSL Ubuntu-24.04, sibling checkouts under `/home/iris-machine/optima-rea`, Compose service **db**, database **IRISBackend**, sqlcmd **/opt/mssql-tools18/bin/sqlcmd**, and the container's **MSSQL_SA_PASSWORD**, supplied as SQLCMDPASSWORD without printing it.

First transfer the reviewed SQL/helper and application source/images to the hosted checkout. Confirm a maintenance window and the intended host; the helper stops api/web, leaves SQL Server running, creates a timestamped COPY_ONLY/CHECKSUM backup in persistent sql_data, verifies it, streams the SQL with `sqlcmd -b`, then verifies columns, trusted FKs and all four unique indexes. It stops on any error and leaves application services stopped.

From **PowerShell on the hosted Windows machine**:

```powershell
& '\\wsl.localhost\Ubuntu-24.04\home\iris-machine\optima-rea\IRISBackend\deploy\Deploy-ClaimInvoicing.ps1'
if (-not $?) { throw 'Stop: review schema deployment before deploying images.' }

# Build/deploy the reviewed backend first using the existing Compose image/build configuration.
wsl.exe -d Ubuntu-24.04 -- bash -lc 'cd /home/iris-machine/optima-rea/IRISBackend/deploy && docker compose build api && docker compose up -d --no-deps api'
if ($LASTEXITCODE -ne 0) { throw 'Backend deployment failed' }
# Then deploy the reviewed frontend.
wsl.exe -d Ubuntu-24.04 -- bash -lc 'cd /home/iris-machine/optima-rea/IRISBackend/deploy && docker compose build web && docker compose up -d --no-deps web'
if ($LASTEXITCODE -ne 0) { throw 'Frontend deployment failed' }
Invoke-RestMethod 'http://192.168.10.2:8080/health/live'
Invoke-RestMethod 'http://192.168.10.2:8080/health/ready'
```

The helper contains the exact backup, streaming sqlcmd and verification commands. It is **not** a startup migration and was **not** executed against the hosted environment. Do not run the unreliable imported EF migration chain. Keep the verified backup and copy it off the Docker volume using the existing backup procedure. A script failure rolls its transaction back. Prefer rolling application images back while keeping the additive schema. A full backup restore requires a reviewed outage and would discard later writes; do not drop new columns after approvals exist or truncate business data.

## Validation and manual acceptance

Final verification:

- Backend solution build passed. Focused claim, legacy workflow, currency-rounding, allocation-calculator and ordinary Avoir-limit regression selection: **29 passed**. The two affected approval/history and real loopback HTTP authorization/approval/download tests passed again after the final changes. HTTP checks cover 401/403, decimal-text approval, ZIP download and identical approval retry.
- Frontend production build passed, with the existing initial-bundle warning (approximately 1.08 MB versus the 900 kB warning budget). Focused selections: **24 claim/money/notification tests + 11 ordinary invoice regressions + 1 Claim source-mapper test + 1 Claim badge/history test = 37 passed**.
- SQL ran twice locally after a verified COPY_ONLY/CHECKSUM backup. All original values in seven tables were unchanged. Hosted PowerShell helper parsed successfully; it was not executed on the host.
- Separate collection-note and cedent Avoir PDFs were generated, rendered to PNG and visually inspected. PDF regeneration kept the original financial record counts. Samples are in `artifacts/claim-invoicing-pdf/`.
- `git diff --check` passed in both repositories. No commit/push/reset/revert/checkout/clean was performed.

These are focused checks, not a full-suite or hosted/browser sign-off. The preceding Avoir handoff records broader PDF/history and receipt-pairing fixture failures; those unrelated broader selections were not rerun here. Initial restricted-environment Angular/LocalDB and package-cache errors were resolved by running the focused builds/tests with normal user access. The final backend solution build is clean. Isolated test databases are disposable; no test financial documents were inserted in the user's development business database.

Manual controlled check after deployment:

1. Front Officer: open a Bound contract, add a claim, select a currency different from the contract; save/reopen. Check null, explicit zero and positive claim deductibles, plus Mr Clippy attachments/retry.
2. Enter dates/nature and approved paid loss; choose Paid Claim. Check one Back Officer notification; repeat save and confirm no duplicate. Use a fresh controlled test record, not an issued business claim.
3. Back Officer: filter/sort/page the shared list, open that notification and verify the exact claim. Confirm create/edit financial fields are unavailable.
4. Click Pass to invoicing. Verify only this contract's confirmed eligible panels, claim currency, independent gross/deductible and approved paid_loss total.
5. Check blank label, excess precision, negative/duplicate/foreign allocations and a one-minor-unit mismatch are rejected. Valid allocations must cover the full approved total.
6. Approve once; verify one Claim-source cedent Avoir, one collection note per panel, Claim-source remittances, locked financial fields, approved user/time and processed notification.
7. Verify ZIP and each individual PDF, including recipient, reference, label, source, claim number, currency, positive amount and no bank section. Re-download; confirm record counts/numbers do not change.
8. Retry identical approval and open a stale preview concurrently; verify reuse or a clear 409. Confirm ordinary contract invoice/Avoir behavior and premium totals remain correct.
9. Net eligible positive Claim remittances against Premium remittances in existing closing/finance workflow; no unrelated disbursement should be manufactured by claim approval.

Product Owner follow-ups (do not block the specified workflow): Confirm a future controlled correction/cancellation workflow for already issued claims and whether a retained portion may ever be permitted. No such correction or retained portion is enabled here.


## Net-loss display follow-up

Claim form, claim history and invoicing overview now display **Net loss = paid loss + expert fees + other fees - claim deductible**. A missing optional claim deductible contributes zero to this derived calculation but remains null in storage. The API exposes a decimal `netLoss` rounded to the claim currency precision; the existing gross component storage and `amount100Percent` API field retain their original meaning for compatibility. No schema change or historical business-data rewrite is needed. The claim form shows the live contract signed share and contract deductible below Status, using contract currency for that deductible. Paid loss remains the approved invoicing amount under the earlier workflow specification.


## Signed-share allocation correction

This correction supersedes the original full-paid-loss allocation rule above. New approvals use **net loss = paid loss + expert fees + other fees - claim deductible**, then apply each eligible confirmed panel's actual signed percentage. Shares are not normalized to 100%. Net loss 15,000 with two 5% panels gives **750 per panel, total 1,500**. The cedent Avoir, collection notes and Claim remittances all reconcile to that covered total. Approval still allows the Back Officer to edit allocations but requires their sum to equal the server-calculated covered total. The aggregate covered amount is rounded to currency precision and residual minor units are assigned deterministically by largest remainder in panel-ID order. A positive net loss and covered amount and combined signed shares of at most 100% are required. Already invoiced claims retain their saved amounts and idempotent results. No schema change or rewrite of existing financial records is needed.

Signed-share correction verification: 20 focused backend tests and 23 frontend tests passed. The frontend production build passed with its existing bundle warning; the backend compiled in isolated output because the development API was running. Generated PDFs were rendered and visually checked: each collection note shows net loss 15,000, share 5% and amount 750; the cedent Avoir shows 1,500. Rebuild/restart the development backend and reopen the invoicing modal to load the updated API. The frontend refuses the old full-loss preview instead of permitting an incorrect approval.


## Shared Avoir document-number follow-up

Claim documents and remittance rows use the existing cedent Avoir's AV-YYYY-###### number from the shared AV sequence. One Avoir number is allocated per Claim approval. Panel IDs distinguish the separate collection-note PDF files without introducing another public document-number sequence. New Claim remittances keep the real source_invoice_id link; older rows resolve the Avoir through INVOICE.Claim_ref. The Back Officer label is displayed from that Avoir.

The later per-paper numbering change was reverted at the user's request. The local note references were restored with a verified backup and an audit of the reversal. Already consumed AV numbers remain reserved; the shared counter is not decremented. No earlier net-loss, signed-share, attachment, sidebar or Avoir amount-limit work is reverted.

Undo verification: the two shared-number/idempotency document tests passed, frontend/backend builds passed, and the restarted API passed live/ready checks. A verified backup was created at `artifacts/database-backups/IRISBackend_before_undo_per_document_numbers_20261119_142552_314.bak`. Only the last task's note-reference updates for notes 3/4 were reversed to their original CCN values; the previous public shared-Avoir display is restored. Claim 4 and all subsequent financial data were preserved. The existing counter remains 19 and used numbers are not recycled. Original assignment audit was retained with explicit reversal entries.


## Collection Note template (23 November 2026)

Collection-note PDFs follow the supplied plain English reference: IRIS logo and company header, recipient, available claim details, share/fee amount, English amount in words and payment instructions. Missing Retrocedant/Original Insured lines are omitted as requested. Type uses the existing main cover (snapshotted for new approvals, read-only fallback for old snapshots). Notes retain their approved recipient allocation and shared Avoir reference.

All three active configured IrisBankAccounts (TND/USD/EUR) appear as complete stacked blocks; overflow continues onto another page instead of truncating identifiers. Incomplete account configuration returns a clear 409 on collection/bundle download. Avoir-only downloads do not require accounts and retain the removal of the lower claim-details box. Fax and website default to the supplied reference values when no branding override is configured. No schema or business-data update is needed.

Verification: 15 focused workflow/financial-renderer tests passed, followed by 8 final PDF/approval checks against the normal build. Reference, per-panel, Avoir and long-content PDFs were rendered and visually checked; the ordinary collection note fits all three accounts on one page. The backend solution builds with zero errors (existing AutoMapper advisory warnings remain). Local Development API was restarted and both /health/live and /health/ready returned 200. No local business records or schema were modified.
