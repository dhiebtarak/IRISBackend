-- Reference data only. Verify the selected database and take a verified backup first.
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF (SELECT COUNT(*) FROM dbo.CURRENCY WITH (UPDLOCK, HOLDLOCK)
        WHERE UPPER(LTRIM(RTRIM(currency_code))) = N'FCFA') > 1
        THROW 50001, 'Multiple FCFA currency records already exist. Manual review is required.', 1;
    IF EXISTS (SELECT 1 FROM dbo.CURRENCY WHERE UPPER(LTRIM(RTRIM(currency_code))) = N'FCFA')
    BEGIN
        UPDATE dbo.CURRENCY SET
            currency_code = N'FCFA', currency_symbol = N'FCFA', name = N'Franc CFA',
            decimal_places = 0, is_active = 1, country = N'West and Central Africa'
        WHERE UPPER(LTRIM(RTRIM(currency_code))) = N'FCFA';
        -- Preserve any previously configured is_default value on an existing FCFA record.
    END
    ELSE
    BEGIN
        INSERT INTO dbo.CURRENCY
            (currency_code, currency_symbol, name, decimal_places, is_default, is_active, country)
        VALUES (N'FCFA', N'FCFA', N'Franc CFA', 0, 0, 1, N'West and Central Africa');
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
SELECT id_currency, currency_code, currency_symbol, name, decimal_places, is_default, is_active, country
FROM dbo.CURRENCY WHERE UPPER(LTRIM(RTRIM(currency_code))) = N'FCFA';
