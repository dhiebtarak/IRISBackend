using IRISBackend.DTOs;
using IRISBackend.Models;
using IRISBackend.Repositories;
using IRISBackend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;
using SecurityClaim = System.Security.Claims.Claim;

namespace IRISBackend.Tests;

public sealed class BankReceiptDocumentAttachmentIntegrationTests : IAsyncLifetime
{
    private readonly string _databaseName = $"IRISBackend_ReceiptAttachmentTests_{Guid.NewGuid():N}";
    private IRISBackendContext _db = null!;

    public async Task InitializeAsync()
    {
        _db = CreateContext();
        await _db.Database.EnsureDeletedAsync();
        await _db.Database.EnsureCreatedAsync();
        await SeedAsync(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task ListBankReceiptDocumentsAsync_WhenReceiptHasNoAttachments_ReturnsEmptyList()
    {
        var service = CreateService("Finance officer");

        var result = await service.ListBankReceiptDocumentsAsync(1);

        Assert.True(result.Succeeded);
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task ListBankReceiptDocumentsAsync_ReturnsOnlyActiveDocumentsForSuppliedReceipt()
    {
        var activeDocument = CreateReceiptDocument(receiptId: 1, isActive: true, status: "Active", fileName: "receipt-active.pdf");
        _db.DocumentAttachments.AddRange(
            activeDocument,
            CreateReceiptDocument(receiptId: 1, isActive: false, status: "Archived", fileName: "receipt-archived.pdf"),
            CreateReceiptDocument(receiptId: 2, isActive: true, status: "Active", fileName: "other-receipt.pdf"),
            CreateUnrelatedDocument());
        await _db.SaveChangesAsync();
        var service = CreateService("Back officer");

        var result = await service.ListBankReceiptDocumentsAsync(1);

        Assert.True(result.Succeeded);
        var document = Assert.Single(result.Value!);
        Assert.Equal(activeDocument.IdDocument, document.IdDocument);
        Assert.Equal("receipt-active.pdf", document.FileName);
        Assert.Equal($"/api/bank-receipts/1/attachments/{activeDocument.IdDocument}/download", document.DownloadUrl);
    }

    [Theory]
    [InlineData("Finance officer")]
    [InlineData("Back officer")]
    public async Task DownloadBankReceiptDocumentAsync_AllowsFinanceAndBackOfficer(string position)
    {
        var attachment = CreateReceiptDocument(receiptId: 1, isActive: true, status: "Active", fileName: "download.pdf");
        _db.DocumentAttachments.Add(attachment);
        await _db.SaveChangesAsync();
        var service = CreateService(position);

        var result = await service.DownloadBankReceiptDocumentAsync(1, attachment.IdDocument);

        Assert.True(result.Succeeded);
        Assert.Equal("download.pdf", result.Value!.FileName);
        Assert.Equal("application/pdf", result.Value.MimeType);
    }

    [Fact]
    public async Task DownloadBankReceiptDocumentAsync_WhenDocumentBelongsToAnotherReceipt_ReturnsNotFound()
    {
        var attachment = CreateReceiptDocument(receiptId: 2, isActive: true, status: "Active", fileName: "other.pdf");
        _db.DocumentAttachments.Add(attachment);
        await _db.SaveChangesAsync();
        var service = CreateService("Finance officer");

        var result = await service.DownloadBankReceiptDocumentAsync(1, attachment.IdDocument);

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
    }

    [Fact]
    public async Task UploadAndArchiveBankReceiptDocumentAsync_AllowsOnlyFinanceOfficer()
    {
        var financeService = CreateService("Finance officer");
        var upload = await financeService.UploadBankReceiptDocumentAsync(1, new DocumentUploadRequestDto
        {
            FileName = "finance-upload.pdf",
            MimeType = "application/pdf",
            ContentBase64 = Convert.ToBase64String([1, 2, 3])
        });

        Assert.True(upload.Succeeded);
        Assert.Equal(StatusCodes.Status201Created, upload.StatusCode);
        var uploaded = await _db.DocumentAttachments.AsNoTracking().SingleAsync(document => document.IdDocument == upload.Value!.IdDocument);
        Assert.Equal(1, uploaded.IdBankReceipt);
        Assert.Null(uploaded.IdContract);
        Assert.Null(uploaded.IdPanel);
        Assert.Null(uploaded.IdCounterparty);
        Assert.Null(uploaded.IdEndorsement);
        _db.ChangeTracker.Clear();

        var backService = CreateService("Back officer");
        var backUpload = await backService.UploadBankReceiptDocumentAsync(1, new DocumentUploadRequestDto
        {
            FileName = "back-upload.pdf",
            MimeType = "application/pdf",
            ContentBase64 = Convert.ToBase64String([1, 2, 3])
        });
        Assert.False(backUpload.Succeeded);
        Assert.Equal(StatusCodes.Status403Forbidden, backUpload.StatusCode);

        var backArchive = await backService.ArchiveBankReceiptDocumentAsync(1, upload.Value!.IdDocument);
        Assert.False(backArchive.Succeeded);
        Assert.Equal(StatusCodes.Status403Forbidden, backArchive.StatusCode);

        var archiveService = CreateService("Finance officer");
        var financeArchive = await archiveService.ArchiveBankReceiptDocumentAsync(1, upload.Value!.IdDocument);
        Assert.True(financeArchive.Succeeded, $"{financeArchive.StatusCode}: {financeArchive.Error?.Message}");
        Assert.Equal(StatusCodes.Status204NoContent, financeArchive.StatusCode);
        var archived = await _db.DocumentAttachments.AsNoTracking().SingleAsync(document => document.IdDocument == upload.Value!.IdDocument);
        Assert.False(archived.IsActive);
        Assert.Equal("Archived", archived.Status);
    }

    private DocumentAttachmentService CreateService(string position)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new SecurityClaim(ClaimTypes.NameIdentifier, "finance-user"),
            new SecurityClaim("position", position)
        }, "Test"));
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };

        return new DocumentAttachmentService(
            new DocumentAttachmentRepository(_db),
            new ContractRepository(_db, NullLogger<ContractRepository>.Instance),
            new ContractPlacementRepository(_db),
            _db,
            new FakeStorageService(),
            http,
            NullLogger<DocumentAttachmentService>.Instance);
    }

    private IRISBackendContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<IRISBackendContext>()
            .UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={_databaseName};TrustServerCertificate=True;MultipleActiveResultSets=True;")
            .Options;
        return new IRISBackendContext(options);
    }

    private static async Task SeedAsync(IRISBackendContext db)
    {
        var user = new AspNetUser
        {
            Id = "finance-user",
            UserName = "finance-user",
            NormalizedUserName = "FINANCE-USER",
            Email = "finance@example.test",
            NormalizedEmail = "FINANCE@EXAMPLE.TEST",
            EmailConfirmed = true,
            PasswordHash = "hash",
            SecurityStamp = "stamp",
            ConcurrencyStamp = "concurrency",
            PhoneNumber = "000",
            PhoneNumberConfirmed = false,
            TwoFactorEnabled = false,
            LockoutEnabled = false,
            AccessFailedCount = 0,
            FullName = "Finance User",
            Department = "Reassurance",
            Position = "Finance officer"
        };
        var currency = new Currency
        {
            CurrencyCode = "USD",
            CurrencySymbol = "$",
            Name = "US Dollar",
            DecimalPlaces = 2,
            IsDefault = true,
            IsActive = true,
            Country = "US"
        };
        var counterparty = new Counterparty
        {
            Name = "Cedent",
            Type = "Cedent",
            Email = "cedent@example.test",
            PhoneNumber = "000",
            AddressLine = "Address",
            City = "City",
            Country = "Country",
            TaxIdentificationNumber = "TAX",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            Activity = "Insurance",
            CedantLegalEntity = "Entity",
            CommercialRegister = "CR",
            ContactName = "Contact",
            LegacyCompanyCode = "LEG",
            Observations = string.Empty,
            PostalCode = "0000",
            Status = "Active",
            Website = "https://example.test"
        };

        db.AspNetUsers.Add(user);
        db.Currencies.Add(currency);
        db.Counterparties.Add(counterparty);
        await db.SaveChangesAsync();

        var account = new CounterpartyBankAccount
        {
            IdCounterparty = counterparty.Id,
            IdCurrency = currency.IdCurrency,
            BankName = "Bank",
            BankCity = "City",
            BankCountry = "Country",
            AgencyCode = "001",
            Rib = "RIB",
            Iban = "TN5900000000000000000000",
            SwiftBic = "ABCDEF12",
            AccountLabel = "Main",
            IsDefault = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = user.Id
        };

        db.CounterpartyBankAccounts.Add(account);
        await db.SaveChangesAsync();

        db.BankReceipts.AddRange(
            CreateReceipt(user.Id, account.IdBankAccount, currency.IdCurrency, counterparty.Id, "BR-1", "Receipt 1"),
            CreateReceipt(user.Id, account.IdBankAccount, currency.IdCurrency, counterparty.Id, "BR-2", "Receipt 2"));
        await db.SaveChangesAsync();
    }

    private static BankReceipt CreateReceipt(
        string userId,
        int bankAccountId,
        int currencyId,
        int counterpartyId,
        string bankReference,
        string settlementLabel) => new()
    {
        IdBankAccount = bankAccountId,
        ReceptionDate = new DateOnly(2026, 10, 1),
        OperationDate = new DateOnly(2026, 10, 1),
        AmountReceived = 100m,
        IdCurrency = currencyId,
        SettlementMethod = "BankTransfer",
        CollectionType = "Premium",
        BankReference = bankReference,
        SettlementLabel = settlementLabel,
        DetailsStatus = "Unpaired",
        Reason = string.Empty,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = userId,
        IdCounterparty = counterpartyId
    };

    private static DocumentAttachment CreateReceiptDocument(int receiptId, bool isActive, string status, string fileName) => new()
    {
        IdBankReceipt = receiptId,
        DocumentType = "BankReceiptAttachment",
        DocumentCategory = "BankReceipt",
        Title = fileName,
        FileName = fileName,
        OriginalFileName = fileName,
        FileExtension = ".pdf",
        MimeType = "application/pdf",
        StorageProvider = "LocalFileSystem",
        FilePath = $"bank-receipts/{receiptId}/{fileName}",
        ConfidentialityLevel = "Internal",
        Status = status,
        IsActive = isActive,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = "finance-user",
        FileSize = 3
    };

    private static DocumentAttachment CreateUnrelatedDocument() => new()
    {
        DocumentType = "UnrelatedAttachment",
        DocumentCategory = "Unrelated",
        Title = "unrelated.pdf",
        FileName = "unrelated.pdf",
        OriginalFileName = "unrelated.pdf",
        FileExtension = ".pdf",
        MimeType = "application/pdf",
        StorageProvider = "LocalFileSystem",
        FilePath = "unrelated/unrelated.pdf",
        ConfidentialityLevel = "Internal",
        Status = "Active",
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = "finance-user",
        FileSize = 3
    };

    private sealed class FakeStorageService : IDocumentStorageService
    {
        public Task<StoredDocument> SaveAsync(string scope, int ownerId, DocumentUploadRequestDto request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new StoredDocument
            {
                StoredFileName = request.FileName,
                OriginalFileName = request.FileName,
                FileExtension = Path.GetExtension(request.FileName),
                MimeType = request.MimeType,
                RelativePath = $"{scope}/{ownerId}/{request.FileName}",
                FileSize = Convert.FromBase64String(request.ContentBase64).LongLength
            });
        }

        public Task DeleteAsync(string? relativePath, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<Stream?> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Stream?>(new MemoryStream([1, 2, 3]));
        }
    }
}
