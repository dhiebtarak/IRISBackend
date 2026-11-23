# Back Officer Avoir amounts — 2026-11-18

Back Officers can enter an Avoir amount independently of the reported available credit balance. A zero, absent or exhausted balance does not cap Avoir preview or creation. Both Pair with Encaissement and Direct to Remittances remain available.

The frontend removes the Avoir maximum and balance-blocking messages, hides Available after Avoir, and retains an existing amount rather than reducing it to the reported balance. A reported positive balance can still provide an initial suggestion when no amount has been entered. Normal Invoice and Invoice Fees retain their remaining-amount maximum.

The backend removes the contract credit-balance and per-panel credit caps for Avoir creation/preview. The shared allocation calculator bypasses its invoiceable-premium maximum only when the server identifies an Avoir. Positive monetary values, currency precision, valid contract financial data, Back Officer permissions, Bound status, an active invoice source, confirmed reinsurers, reconciliation, processing-mode requirements and idempotency remain enforced. Allocation and outgoing-remittance signs remain backend-authoritative and negative for Avoirs. Existing receipt pairing/refund settlement rules are unchanged.

Changed production files:

- FrontAuthIRIS: `src/app/contracts/contract-overview/contract-overview.component.ts` and `.html`.
- IRISBackend: `IRISBackend/Services/FinancialWorkflowService.cs` and `PremiumDebitNoteAllocationCalculator.cs`.

Focused verification: 12 frontend tests and 18 backend tests passed. Coverage includes amounts above contract/panel balances, repeated Avoirs at zero remaining credit, both processing modes, signed outgoing rows, malformed amounts/currency precision, and ordinary invoice limits. The frontend production build passed with the existing initial-bundle budget warning; the backend solution build passed. Integration tests used isolated temporary LocalDB databases.

A broader regression selection also encountered existing PDF/document-history, receipt-pairing and saved-balance fixture failures. No full-suite pass is claimed. Changed cap-related expectations were updated and are included in the passing focused tests.

No database schema change or deployment SQL is needed. Deploy the frontend and backend code together. No existing application business records were changed, and no commit or push was performed. The verified local backend process was stopped to release its build DLL lock; restart it through Visual Studio after review.
