# Counterparty contacts and bank identifiers — test deployment

`20261111_test_schema_updates.sql` is the latest standalone SQL Server deployment covering all three requests (and superseding the November 10 bundle):

- Creates `dbo.COUNTERPARTY_CONTACT` with its counterparty foreign key and ordering index. Existing primary contact columns are unchanged.
- Widens `COUNTERPARTY_BANK_ACCOUNT.Iban`, `COUNTERPARTY_BANK_ACCOUNT.Rib` and `DISBURSEMENT.Iban_snapshot` to `nvarchar(max)`, preserving their existing nullability and values.
- Widens primary/correspondent SWIFT/BIC fields and both payment snapshots to `nvarchar(max)`. The matching application accepts blank or arbitrary IBAN and SWIFT/BIC values without field validators.

The script is repeatable and runs both changes in one transaction. It contains no database selection and does not drop tables, delete records or rewrite existing identifiers. The individual scripts and matching EF migrations are also included. Migration upgrade SQL is repeatable, so a database updated using this SQL can subsequently record the migrations without recreating the table. Do not apply unrelated pending migrations without reviewing them.

## Run against the confirmed test database

1. Identify and verify the exact test SQL Server and database, using `SELECT @@SERVERNAME, DB_NAME();`. Do not use a production, office or unidentified target.
2. Stop the test backend during the schema change.
3. Take a database backup using your established SQL Server backup procedure, preferably `COPY_ONLY, CHECKSUM`, and verify it with `RESTORE VERIFYONLY ... WITH CHECKSUM`. Proceed only after successful verification.
4. From this directory, with an authorized integrated-authentication account, run:

```powershell
sqlcmd -S '<confirmed-test-server>' -d '<confirmed-test-database>' -E -b -i '.\20261111_test_schema_updates.sql'
```

Use the environment's approved authentication mechanism if integrated authentication is unavailable; never put passwords in repository files or command history.

5. Deploy/restart the matching updated backend and frontend together.
6. Verify the columns using the query below, then create/edit a counterparty with two contacts and a bank account with a hyphenated identifier longer than 100 characters. Reopen both records and verify the complete values. Check that selecting the account for payment succeeds.

```sql
SELECT OBJECT_NAME(c.object_id) AS TableName, c.name AS ColumnName,
       TYPE_NAME(c.system_type_id) AS DataType, c.max_length, c.is_nullable
FROM sys.columns c
WHERE (c.object_id = OBJECT_ID(N'dbo.COUNTERPARTY_BANK_ACCOUNT') AND c.name IN (N'Iban', N'Rib', N'Swift_bic', N'Correspondent_swift_bic'))
   OR (c.object_id = OBJECT_ID(N'dbo.DISBURSEMENT') AND c.name IN (N'Iban_snapshot', N'Swift_bic_snapshot', N'Correspondent_swift_bic_snapshot'));
-- Expect nvarchar and max_length = -1 for all seven columns.
SELECT OBJECT_ID(N'dbo.COUNTERPARTY_CONTACT', N'U') AS ContactTable;
```

No test server/database was supplied in the request, so this package has not been executed against a remote test environment. The combined script is covered by an isolated SQL Server integration test, including deployment from the old column sizes, repeated execution, long account/payment values and preservation of additional contacts.

IBAN and SWIFT/BIC inputs (including correspondent SWIFT/BIC) have no required, format or length validators when creating or updating a bank account. Blank values and values such as `29590495` are accepted. RIB has no field-specific length limit. Existing normalization is retained; nonempty IBAN duplicate-account checks, authorization, ownership, currency and masking rules remain in place. Removing field validators does not change the separate requirements for starting a payment with complete bank details.

Migration rollback refuses to shorten bank columns while identifiers longer than the old sizes exist. Never truncate values to force a rollback.
