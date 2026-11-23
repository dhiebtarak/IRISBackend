using Microsoft.EntityFrameworkCore.Migrations;

namespace IRISBackend.Migrations;

public sealed partial class AddClaimDocumentAttachments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
-- Additive and repeatable. Verify the target environment and take a backup before applying.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.DOCUMENT_ATTACHMENT', N'U') IS NULL OR OBJECT_ID(N'dbo.CLAIM', N'U') IS NULL
    THROW 51000, 'Required attachment or claim table is missing.', 1;
IF COL_LENGTH(N'dbo.DOCUMENT_ATTACHMENT', N'Id_claim') IS NULL
    ALTER TABLE dbo.DOCUMENT_ATTACHMENT ADD Id_claim int NULL;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.DOCUMENT_ATTACHMENT') AND name=N'FK_DOCUMENT_ATTACHMENT_CLAIM')
    EXEC(N'ALTER TABLE dbo.DOCUMENT_ATTACHMENT WITH CHECK ADD CONSTRAINT FK_DOCUMENT_ATTACHMENT_CLAIM FOREIGN KEY(Id_claim) REFERENCES dbo.CLAIM(Claim_ref)');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.DOCUMENT_ATTACHMENT') AND name=N'IX_DOCUMENT_ATTACHMENT_Id_claim')
    EXEC(N'CREATE INDEX IX_DOCUMENT_ATTACHMENT_Id_claim ON dbo.DOCUMENT_ATTACHMENT(Id_claim)');
COMMIT TRANSACTION;
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new System.InvalidOperationException("Claim attachments contain business records. Use a reviewed data-preserving rollback or restore a verified backup.");
}
