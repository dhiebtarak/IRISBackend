# Claims workflow — 11 November 2026

## Scope and schema inspection

Repositories: `C:\Users\USER\Desktop\FrontAuthIRIS` and `C:\Users\USER\Desktop\IRISBackend`. Both were readable/writable and clean when implementation started. No reset, revert, checkout, clean, commit or push was performed.

The effective Development database was verified as `(localdb)\MSSQLLocalDB` / `IRISBackend`, reporting server `IRISTNLT40\LOCALDB#43A1E514`, with LocalDB, local machine identity and local data-file paths confirmed. Read-only inspection covered actual column types, precision, nullability, foreign keys, indexes and receipt-allocation check constraints before implementation. `CLAIM` and `COLLECTION_NOTE` already existed, with zero rows in each. Those tables were reused.

Existing fields cover claim identity, dates, description, estimated/reserve loss, paid loss, expert fees, deductible, currency and underwriting year. Added fields cover other fees, the signed-share/collection calculation, processing mode, issuance metadata and immutable document snapshots. Collection notes now identify their reinsurer panel and currency. Unique indexes prevent duplicate note lines, duplicate active claim pairings and duplicate active panel remittances. The legacy `COLLECTION_NOTE.id_claim` and `claim_ref` foreign keys are preserved and populated consistently.

## Agreed financial rules

- 100% amount = paid loss + expert fees + other fees. Reserve is recorded separately.
- Collection amount = round(max(100% amount − contract deductible, 0) × contract signed share / 100), using the contract currency precision.
- A loss at or below the deductible becomes `UnderDeductible` when submitted for processing, gets a closing date, and creates no financial note/remittance or Back Office notification.
- `PaidLoss` means complete and ready for Back Office; it does not assert a completed bank transfer.
- The server derives the deductible, signed share, permitted currency, underwriting year and audit actors. The client cannot submit replacements for them.
- Underwriting year currently uses the contract inception year. The existing free-text `UnderwritingYearBasis` field is not an implemented year-calculation rule. Currency is restricted to the one currency currently stored on each contract.
- The existing facultative deductible is the available contract-level source. Missing deductible or signed share blocks processing; it is never silently replaced with zero. An incomplete Outstanding claim can still be recorded.

Example covered by SQL integration tests: paid loss 10,000 + fees 500 − deductible 1,000, at 10% signed share, produces a 950 collection amount. A reserve of 99,999 does not enter that calculation.

## User workflow

1. Open a Bound contract as a user with Claims access. **Add Claim** appears beside Contract documents in the top-right action area. Draft and other phases do not offer it; the backend also enforces Bound status.
2. Enter dates, nature of loss, contract currency, reserve, paid loss and fees. The read-only 100% preview is calculated from paid loss and fees. Choose Outstanding while information is incomplete or Paid loss when complete.
3. Claims History appears above Invoice History and shows claim reference, loss date, UY, reserve, paid loss, status and available actions. It is server-paged. A separate Claims workspace lists claims across contracts.
4. Transitioning to Paid loss creates an existing-style Back Officer notification pointing to `/workspace/reassurance/back-officer/claims`. Saving an already-Paid claim again does not duplicate the notification.
5. Back Office reviews the claim and issues the collection note, choosing either direct remittances or Encaissement pairing. The confirmed, active reinsurer panel shares must reconcile to the claim's signed share.
6. Issuance stores the generated PDF and financial snapshots. Downloads return the same stored document. Issued claim financials are locked. Retrying issuance is idempotent; attempting a different mode after issuance returns conflict.
7. Direct processing creates one negative claim credit per confirmed reinsurer panel. Pairing waits for a same-cedent, same-contract, same-currency Encaissement before creating those credits.
8. Claim credits use the existing Avoir-style netting, closing and Finance payment path. The integration test offsets a 950 claim against a 1,000 premium and confirms a 50 payment through Finance. Standalone claim credits cannot bypass netting as positive payments.
9. Issued claims can be closed after their active remittances are settled. Under-deductible claims are terminal and logically closed.

For Encaissement pairing, the claim is a credit adjustment, not additional cash received: its existing `RECEIPT_ALLOCATION` row uses `Target_type=CLAIM`, `claim_ref`, a negative allocated amount, and no premium/invoice/contract target IDs, preserving the existing target check constraint. The credit offsets premium allocations and increases the receipt's amount available for netting. No synthetic premium invoice or Avoir invoice is created for a claim.

`GET /api/claims?soaOnly=true` returns only issued, positive-collection Paid/Closed claims. Outstanding, unissued and Under-deductible claims are excluded. No standalone SOA report existed in the inspected code, so none was invented by this change.

## API

- `GET /api/claims?contractId=&page=1&pageSize=20&soaOnly=false`
- `GET /api/claims/contracts/{contractId}/context`
- `POST /api/claims/contracts/{contractId}`
- `PUT /api/claims/contracts/{contractId}/{claimId}`
- `POST /api/claims/{claimId}/collection-note` with `processingMode` of `DIRECT_TO_REMITTANCES` or `PAIR_WITH_ENCAISSEMENT`
- `GET /api/claims/{claimId}/collection-note/pdf`
- `GET /api/claims/{claimId}/receipts` — latest 100 eligible Encaissements
- `POST /api/claims/{claimId}/pair` with the actual `bankReceiptId`
- `POST /api/claims/{claimId}/close`

Creation/editing requires the Claims module. Issuance and pairing require BackOffice. Read/close/document requests independently enforce Claims or BackOffice access. Existing Admin/module rules remain in force. Real HTTP tests cover 401, 403, invalid-date 400 and authorized claim creation 201. No credentials, tokens or banking identifiers are recorded in this report.

## Main implementation files

Frontend:

- `src/app/claims/contract-claims.component.ts`, `.html`, `.spec.ts`
- Existing Claims workspace component/template/spec, replacing its Coming Soon content
- `src/app/shared/models/claim.model.ts`
- `src/app/shared/services/claims.service.ts`
- Existing contract-overview component/template/spec: Bound-only action and history placement
- Existing routes and Back Officer navigation
- Existing financial workspace: claim credit totals and source label

Backend:

- `IRISBackend/Models/Claim.Workflow.cs`
- `IRISBackend/Models/IRISBackendContext.Claims.cs` and its existing partial-context registration
- `IRISBackend/DTOs/ClaimWorkflowDtos.cs`
- `IRISBackend/Controllers/ClaimsController.cs`
- `IRISBackend/Services/FinancialWorkflowService.Claims.cs` and existing service interface
- Existing financial workflow/remittance partials: claim credit signs and shared netting/payment guards
- `IRISBackend/Migrations/20261111152929_AddClaimsWorkflow.cs`, designer and model snapshot
- `IRISBackend/Deployment/20261111_claims_workflow.sql`
- `IRISBackend/Deployment/20261111_claims_test_schema_updates.sql`

Tests extend `OutgoingRemittanceIntegrationTests.cs` and the real-HTTP fixture in `FinanceWorkspaceImprovementsTests.cs`. The new financial implementation is a partial of the existing service, not a parallel financial engine.

## Deployment

Local Development deployment completed after a `COPY_ONLY, CHECKSUM` backup and successful `RESTORE VERIFYONLY ... WITH CHECKSUM`. Backup: `artifacts/database-backups/IRISBackend_before_claims_workflow_20261111_155201_184.bak`. The verified local backend was stopped, the additive script applied, existing financial-table data hashes checked, and the updated backend restarted in Development on its existing local ports.

Existing business records were unchanged. Claims and collection-note counts remained zero after deployment; test claims were confined to isolated GUID-named test databases, which were cleaned up.

For TEST, use `IRISBackend/Deployment/20261111_claims_test_schema_updates.sql`. It combines contacts, bank-field storage and claims changes in repeatable SQL transactions. It has no hard-coded database selection. Confirm the exact test server/database, stop that backend, take and verify its backup, and then run from the Deployment directory:

```powershell
sqlcmd -S '<confirmed-test-server>' -d '<confirmed-test-database>' -E -b -i '.\20261111_claims_test_schema_updates.sql'
```

Restart the matching test backend/frontend after deployment. Review any existing duplicate claim references or active claim allocations before deployment: the unique indexes intentionally reject inconsistent legacy data instead of modifying it. No remote test, office or production database was targeted. No test target was supplied, so remote application remains unperformed.

The EF upgrade delegates to the same repeatable claims SQL, so it can record the migration after a manually deployed schema without recreating it. Downgrade deliberately refuses to remove financial history; use a reviewed recovery procedure or the verified backup instead of dropping claim data. Unrelated Access Activity migration operations were excluded from this claims migration.

## Verification

- Backend build: passed with zero warnings/errors.
- Selected backend/financial tests: **35 passed**, followed by **3 passing targeted checks** for the final currency-unit allocator (36 distinct tests across these runs). Includes claim formulas, equal/below deductible, reserve separation, statuses, immutable PDF, retry protection, actual cross-currency receipt rejection, negative receipt credits, shared closing/Finance payment, repeatable deployment, and HTTP permissions/date binding; also premium/Avoir calculation, PDF-renderer, bank-eligibility and existing explicit-payment-account regressions. Allocation uses whole currency units and deterministic largest-remainder distribution, preventing a negative final reinsurer line on very small claims.
- Claims frontend tests and Bound-only overview action: **9 passed**; 55 unrelated overview tests were excluded by the focused filter.
- Angular production build: passed. Existing initial-bundle warning remains (approximately 1.05 MB versus the 900 kB warning budget, below the 1.2 MB error budget).
- Generated collection PDF rendered with PDFium and visually inspected: one clean page, 950 total and two 475 reinsurer lines, with currency, dates, deductible, signed share and separate reserve correctly presented.
- `git diff --check`: passed.

Representative commands:

```powershell
dotnet test IRISBackend.Tests/IRISBackend.Tests.csproj --no-restore -p:OutDir=<isolated artifacts output> --filter '<Claims workflow and selected related financial tests>'
npm.cmd test -- --watch=false --include=src/app/claims/contract-claims.component.spec.ts --include=src/app/claims/claims-coming-soon.component.spec.ts --include=src/app/contracts/contract-overview/contract-overview.component.spec.ts --filter='ContractClaimsComponent|ClaimsComingSoonComponent|shows Add Claim only'
npm.cmd run build
```

The full repository suites were not rerun or claimed green. Browser interaction with the live business-data UI was not used to create test claims; functional checks ran against isolated SQL/HTTP and Angular component fixtures.

## Manual check

Refresh the browser. On a Bound contract with a defined deductible and signed share, add an Outstanding claim and reopen it. Complete its details and mark Paid loss; confirm the Back Office notification and Claims queue. Issue the collection note using each processing mode on separate claims. Verify the PDF, invoice-independent claim history, credit amounts, Encaissement ownership/currency restrictions and shared closing/Finance payment. Repeat issuance/pairing to verify no duplicate entries. Finally test a claim exactly at the deductible and verify Under-deductible status, logical closure and absence from financial/SOA output.


## Claim attachments (2026-11-12)

Each saved claim now has the shared Document Assistant paperclip, scoped by `claimId`. Front Officer Claims access can manage attachments; Back Office can manage attachments on claims handed over as Paid loss or already issued. Upload/download/archive check the claim and document relationship on the server. Archived files keep their metadata and cannot be downloaded.

Claims accept any file extension (including files with no extension), with the existing 10 MB server limit. Known formats retain content-signature validation; unknown formats download as application/octet-stream. Existing contract, placement and receipt file restrictions are unchanged.

Schema: nullable DOCUMENT_ATTACHMENT.Id_claim, trusted FK to CLAIM.Claim_ref without cascading deletion, and index IX_DOCUMENT_ATTACHMENT_Id_claim. The EF migration is 20261112090000_AddClaimDocumentAttachments. For a test environment, run Deployment/20261112_claim_attachments.sql after the existing claims workflow deployment, against the explicitly selected database after backup. The SQL is additive and repeatable and performs no data updates or deletion.

Applied only to Development: (localdb)\MSSQLLocalDB / IRISBackend, actual server IRISTNLT40\LOCALDB#43A1E514. Verified backup: artifacts/database-backups/IRISBackend_before_claim_attachments_20261112_084147_419.bak. Executed twice; original attachment values hashed before/after and preserved; trusted FK verified. No remote database was changed.

Verification: production Angular build passed (existing initial-bundle budget warning); 33 focused frontend tests passed; 23 focused backend attachment/storage/access tests passed against isolated LocalDB test databases. Final solution build passed. Backend left stopped for Visual Studio restart.
