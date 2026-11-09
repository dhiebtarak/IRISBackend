# Finance Officer workspace implementation — 6 November 2026

## Scope and repositories

Frontend: `C:\Users\USER\Desktop\FrontAuthIRIS`.
Backend: `C:\Users\USER\Desktop\IRISBackend`.
Both repositories were checked separately before editing; directory read/write checks succeeded. Frontend initially had no uncommitted changes. Backend already contained Administrator, module-permission and Access Activity work, including overlapping controller changes and an outgoing-remittance test edit. Those changes were preserved. No reset, revert, checkout, clean, commit or push was performed.

## Root causes and resulting behavior

- Encaissement creation accepted a month and constructed its first day. The form now sends a complete `operationDate` as `YYYY-MM-DD`, bound to `DateOnly` and stored in the existing SQL `date` column. Display formatting treats it as a calendar date, preventing timezone shifts. `reception_date` retains its existing recording-date meaning. Settlement labels are retained separately from business references.
- Incoming Remittance filtering exposed an internal counterparty ID. A reusable, debounced and cancellable directory picker now resolves name, code or email to the ID. Editing or clearing its text immediately clears the old ID. Currency, pairing status, inclusive operation-date bounds, amount bounds, sorting, pagination and reset are applied by the backend. Invalid ranges return validation errors. Receipt count and page retrieval use two SQL queries, with no per-row fetches.
- The database already supported multiple accounts. The management UI did not provide a complete independent add/list workflow; payment selection also rejected ambiguous accounts without allowing an explicit choice. Management now lists active and inactive accounts, offers Add another bank account, view/edit, default and activation controls. Payment contexts accept an explicit eligible account. An account already attached to a payment remains pinned.
- Receipt bank options use the existing paginated options endpoint rather than fetching accounts for an initial subset of counterparties. A selected account remains selected when search pages change.

## Files changed by this task

Paths below are relative to the respective repository. Other working-tree changes belong to the preserved work described above.

Frontend implementation:

- `src/app/app.routes.ts` — lazy-load financial pages, retaining paths and guards and keeping the production initial bundle below its error budget.
- `src/app/shared/models/counterparty.model.ts`
- `src/app/shared/models/financial-workflow.model.ts`
- `src/app/shared/models/calendar-date.util.ts` — calendar validation and formatting.
- `src/app/shared/services/financial-workflow.service.ts`
- `src/app/shared/components/counterparty-autocomplete/counterparty-autocomplete.component.ts`
- `src/app/workspaces/reassurance/financial-workspace/financial-workspace-page.component.ts`
- `src/app/workspaces/reassurance/financial-workspace/financial-workspace-page.component.html`

Backend implementation:

- `IRISBackend/Controllers/FinanceController.cs`
- `IRISBackend/Controllers/FinancialAccountsController.cs`
- `IRISBackend/DTOs/FinancialWorkflowDtos.cs`
- `IRISBackend/Repositories/CounterpartyQueryBuilder.cs`
- `IRISBackend/Services/IFinancialWorkflowService.cs`
- `IRISBackend/Services/FinancialWorkflowService.cs`
- `IRISBackend/Services/FinancialWorkflowService.ClosingFinance.cs`
- `IRISBackend/Services/FinancialWorkflowService.BankAccounts.cs` — partial of the existing service.
- `IRISBackend/Services/BankAccountEligibility.cs`
- `IRISBackend/Validators/BankAccountRequestValidator.cs`
- `IRISBackend/Validators/FinancialReceiptValidator.cs`

Tests:

- Backend: `IRISBackend.Tests/FinanceWorkspaceImprovementsTests.cs`, `BankAccountEligibilityTests.cs`, and targeted additions/contract updates in `OutgoingRemittanceIntegrationTests.cs`.
- Frontend: `counterparty-autocomplete.component.spec.ts`, `finance-workspace-contracts.spec.ts`, `finance-workspace-improvements.spec.ts`, and contract expectation updates in `financial-workflow.service.spec.ts` and `financial-workspace-page.component.spec.ts`.

Database: no schema, migration, deployment-script or historical-data changes.

## API contracts

New endpoints, under `/api/counterparties/{counterpartyId}/bank-accounts`:

- `GET /{bankAccountId}` — authorized edit/detail response; full identifiers are limited to this detail contract.
- `PATCH /{bankAccountId}/default` — transactional default selection.

Updated existing endpoints:

- `GET /api/counterparties` — human-readable case-insensitive directory search.
- `GET /api/finance/bank-account-options` — optional counterparty scope and paginated search.
- `GET /api/finance/bank-receipts` — validated combined filters, stable SQL paging/sorting and display metadata.
- `POST /api/finance/bank-receipts` — complete `operationDate`; selected counterparty/account ownership validation. A legacy month-only request is rejected rather than assigned an invented day.
- Bank-account collection `GET` and `POST`, item `PUT`, and item `/status` `PATCH` — all-account management, validation, duplicate checks and default consistency.
- `GET /api/finance/remittances/{id}/disbursement-context` and `GET /api/finance/closings/{closingId}/disbursement-context` — optional `bankAccountId` query.
- Corresponding `POST .../start-disbursement` routes — optional `{ bankAccountId }` body. Existing empty-body callers remain supported.

## Development database inspection

Effective environment: Development. Configured local instance: `(localdb)\MSSQLLocalDB`; database: `IRISBackend`. SQL reported `IRISTNLT40\LOCALDB#7A448DD3`, with LocalDB identity confirmed and data/log files under the local user's directory. No credentials or connection strings are included here.

Read-only inspection confirmed:

- `BANK_RECEIPT.operation_date` and `reception_date` are already non-null SQL `date` columns.
- Counterparty bank accounts already have the required one-to-many ownership structure.
- Existing unique filtered index `UX_COUNTERPARTY_BANK_ACCOUNT_default_currency` covers `(id_counterparty, id_currency)` where active and default flags are true.
- No normalized duplicate-IBAN groups or duplicate active-default groups were found.
- Ten existing receipts had operation day 1. These dates were left unchanged; their true historical day cannot be inferred safely.

No deployment script or additional index is required. The development business database was not written by this implementation. SQL integration tests created and cleaned isolated GUID-named databases. Production, office and remote databases were not targeted.

## Default-account and payment rules

There is at most one active default per counterparty and currency. The first active account becomes default. Explicitly making another active account default clears the previous flag and selects the new account in one transaction. Deactivating the default promotes the remaining active account with the lowest bank-account ID; with no active accounts, there is no active default. Inactive accounts cannot be defaults.

Mutations use serializable transactions and a parameterized per-counterparty SQL application lock. Old default flags are persisted before replacement flags within the transaction to respect the existing filtered unique index. Failure rolls back the entire switch. Normalized IBAN duplicates within a counterparty, including inactive accounts, return conflict.

Payment selection accepts an explicitly chosen active, owned, currency-compatible account with the required bank details. Otherwise it selects the sole eligible account or unique eligible default. An ambiguous set without a default requires selection. Existing attached payment accounts are preserved.

## Validation and security

Existing JWT/module policies and controller authorization were preserved. Tests exercise HTTP 401, 403, invalid-date 400 and authorized creation. Audit actors come from authenticated server claims. IDs, positive amounts, real calendar dates, ranges, paging limits and sorting allowlists are validated server-side; zero amount bounds are retained correctly.

Bank-account fields have length and format validation; IBANs are whitespace-normalized and uppercased before duplicate checking. The existing payment domain requires IBAN and SWIFT/BIC; RIB remains optional metadata. Lists and selectors mask banking identifiers, including short values. Authorized detail retrieval supplies full values for editing. Accounts cannot be reassigned to another counterparty, and currency changes on referenced accounts return conflict. Deactivation preserves references; no account deletion was introduced. Field/API errors appear inside management and receipt forms, with request/loading guards.

## Verification and remaining failures

Commands were run from their respective repository roots. Angular and LocalDB checks needed execution outside the restricted sandbox; no approval rejection occurred.

- `dotnet build IRISBackend/IRISBackend.csproj --no-restore -p:OutDir=<isolated artifacts directory>`: passed, zero warnings/errors.
- Focused `dotnet test IRISBackend.Tests/IRISBackend.Tests.csproj --no-restore -p:OutDir=<isolated artifacts directory> --filter <focused filter>`: **32 passed, zero failed**. Filter covers `FinanceWorkspaceImprovementsTests`, `BankAccountEligibilityTests`, `CounterpartyQueryBuilderTests`, `FinancialAccessRulesTests`, and `ClosingPaymentAcceptsAnExplicitEligibleAccountAndPreservesItOnRetry`.
- `npm.cmd run build`: passed on final code. Initial bundle approximately 1.03 MB; the 900 kB warning budget remains exceeded by 130.75 kB, below the 1.2 MB error budget.
- `node_modules/.bin/tsc.cmd --noEmit -p tsconfig.spec.json`: passed.
- `npm.cmd test -- --watch=false --include=<three new focused spec paths>`: **21 passed**. Those same tests also passed in the final combined run.
- Final combined Angular run, adding the existing workspace, financial-service and counterparties-service specs: **109 passed, 3 failed out of 112**, plus one unhandled assertion.
- Broader related backend finance run: **116 passed, 22 failed out of 138**.
- `git diff --check` in both repositories: passed; backend emitted existing line-ending conversion notices.

The full related suites are not green. The remaining failures concern existing invoice/Avoir/PDF/pairing/allocation behavior outside the changed workflows. A clean pre-task regression baseline was not captured, so they are not independently proven pre-existing failures. Their assertions were not weakened to make this work pass.

Frontend failures:

1. Invoice download test counts two buttons but desktop/mobile rendering yields four.
2. Avoir download test counts one button but rendering yields two.
3. Partial-pairing display test expects an invoice number absent from its remittance fixture.
4. Unhandled service-spec assertion at `financial-workflow.service.spec.ts:216`: eligible-invoice normalization includes an additional `idInvoice` property.

Backend failure details are recorded in `artifacts/finance-test-results/regression-failures.json`; complete results are in `finance-regression.trx`. Focused results are in `finance-focused-final.trx`. Final Angular output and build evidence are in `frontend-related-final.log` and `frontend-build-final.log` in the same ignored artifacts directory. The backend failures include saved panel allocation prerequisites, pairing-status expectations, credit/Avoir rules, PDF content and currency rounding. See the appended exact test list below.

## Manual verification steps

Browser verification has not been performed in this task. After restarting the backend/frontend with the existing Development configuration:

1. Sign in as an authorized Finance Officer with the relevant modules. Open Encaissement and create a receipt for an exact day such as `2026-09-23`, selecting a real eligible bank account. Save and reload; verify the operation day is unchanged and the reception date retains its separate recording meaning.
2. Find a counterparty by name, business code and email. Select it, edit the text, and clear it; verify no stale selected ID remains. Combine currency, pairing status, inclusive date bounds and amount bounds. Check sorting, paging, reset to page one and reversed-range feedback.
3. Open Counterparty Bank Accounts. Verify inactive accounts are also listed. Use Add another bank account to save a distinct account and verify the existing account remains. Check view/edit, inline errors, normalized duplicate rejection and double-submit protection.
4. Set a different active account as default. Verify one default per currency. Deactivate it and verify the lowest-ID remaining active account becomes default, while prior receipts retain their references.
5. Start a closing/remittance payment using an eligible nondefault account. Reload/retry and verify that the attached account stays fixed. Verify a foreign-counterparty or currency-incompatible account cannot be used.
6. Verify unauthorized users cannot call the APIs directly and lists mask banking identifiers. Full bank detail should appear only through authorized management detail.

## Product assumptions

- Existing historical day-1 values require a trusted correction source and Product Owner approval before any repair; no day was guessed.
- Default scope is counterparty plus currency, matching the existing unique index; deterministic replacement uses the lowest remaining active account ID.
- Existing IBAN/SWIFT payment requirements and reception-date policy are retained. RIB-only payment support would require a separately specified domain change.
- No further clarification blocked the implementation above. Wider regression failures remain separately reviewable work.

### Exact backend regression failures

- `ConcurrentClosingsReserveSelectionOnlyOnce`
- `PairingUsesSavedInvoiceAllocationInsteadOfLivePanelPremium`
- `ClosingRejectsMixedReinsurersOrCurrencies(mixedCurrency: True)`
- `AvoirOffsetsPairingWithoutCreatingPositiveReceiptAllocationAndRetryIsIdempotent`
- `BackOfficerInvoiceDownloadReturnsPersistedDebitNotePdf`
- `BackOfficerAvoirPdfUsesPersistedAmountsAndNegativeSignsWithoutBankAccountDependency`
- `AvoirRejectsCreditExceedingOneConfirmedReinsurersRemainingInvoicedAllocation`
- `ClosingPersistsLinesNotifiesFinanceAndRegeneratesSamePdf`
- `ClosingLineRoundingUsesCurrencyPrecisionAndReconcilesTheRemainder`
- `ContractInvoiceWorkflowCreatesInvoiceFeesAndAvoirWithIndependentNumbersAndSignedTotals`
- `FullPairingGeneratesEarlierPartialAllocationsToo`
- `InvoiceContextUsesFullContractGrossWithoutApplyingCedentSignedShare`
- `MixedPremiumAndAvoirPairingCreatesSeparateSignedPanelRemittances`
- `ContractDocumentRemainsDebitNoteUntilActiveAvoirExists`
- `UniqueConstraintRejectsDuplicateOriginPanel`
- `AvoirRequiresConfirmedReinsurerPanels`
- `ClosingPdfUsesAvoirDirectionAndDisplaysTheAbsoluteRemittanceAmount`
- `AvoirRefundTracksPartialUseAndRejectsCurrencyMismatch`
- `ContractFinancialNoteIsGeneratedFromContractWithoutInvoiceSelection`
- `AvoirCreationAcceptsExactAvailableCreditAndReturnsRefreshedTotals`
- `ContractInvoiceHistoryUsesSignedShareAndRunningInvoiceBalance`
- `RemittanceInsertFailureRollsBackAllocationStatusesAndRequestRecord`
