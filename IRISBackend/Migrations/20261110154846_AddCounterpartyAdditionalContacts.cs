using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace IRISBackend.Migrations;

public partial class AddCounterpartyAdditionalContacts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Repeatable because local installations can deploy the matching SQL script first.
        migrationBuilder.Sql("""
-- Additive and repeatable. Existing primary email/phone columns remain unchanged.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.Counterparties', N'U') IS NULL
    THROW 51000, 'Counterparties table is missing.', 1;
IF OBJECT_ID(N'dbo.COUNTERPARTY_CONTACT', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.COUNTERPARTY_CONTACT (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_COUNTERPARTY_CONTACT PRIMARY KEY,
        CounterpartyId int NOT NULL,
        SortOrder int NOT NULL,
        Email nvarchar(256) NULL,
        PhoneNumber nvarchar(32) NULL,
        CONSTRAINT FK_COUNTERPARTY_CONTACT_Counterparties_CounterpartyId
            FOREIGN KEY (CounterpartyId) REFERENCES dbo.Counterparties(Id) ON DELETE CASCADE
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.COUNTERPARTY_CONTACT')
    AND name = N'IX_COUNTERPARTY_CONTACT_CounterpartyId_SortOrder')
    CREATE INDEX IX_COUNTERPARTY_CONTACT_CounterpartyId_SortOrder
        ON dbo.COUNTERPARTY_CONTACT(CounterpartyId, SortOrder);
COMMIT TRANSACTION;

""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "COUNTERPARTY_CONTACT");
    }
}