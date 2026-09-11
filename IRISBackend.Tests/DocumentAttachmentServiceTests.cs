using IRISBackend.DTOs;
using IRISBackend.Models;
using IRISBackend.Repositories;
using IRISBackend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using System.Linq.Expressions;
using System.Security.Claims;
using Xunit;
using SecurityClaim = System.Security.Claims.Claim;

namespace IRISBackend.Tests;

public sealed class DocumentAttachmentServiceTests
{
    [Fact]
    public async Task ArchiveAsync_ContractDocumentUpdatesAuditFieldsAndKeepsStorageMetadata()
    {
        var document = CreateDocument(10, 5, null);
        var repository = new FakeDocumentRepository(document);
        var service = CreateService(repository, accessibleContractId: 5);

        var result = await service.ArchiveAsync(document.IdDocument);

        Assert.True(result.Succeeded);
        Assert.Equal(StatusCodes.Status204NoContent, result.StatusCode);
        Assert.False(document.IsActive);
        Assert.Equal("Archived", document.Status);
        Assert.Equal("user-1", document.UpdatedBy);
        Assert.NotNull(document.UpdatedAt);
        Assert.Equal("contracts/5/generated.pdf", document.FilePath);
        Assert.Equal(5, document.IdContract);
        Assert.Null(document.IdPanel);
    }

    [Fact]
    public async Task ArchiveAsync_PanelDocumentValidatesPanelOwnershipAndKeepsRelationship()
    {
        var document = CreateDocument(11, 5, 7);
        var repository = new FakeDocumentRepository(document);
        var service = CreateService(repository, accessibleContractId: 5, accessiblePanelId: 7);

        var result = await service.ArchiveAsync(document.IdDocument);

        Assert.True(result.Succeeded);
        Assert.Equal(StatusCodes.Status204NoContent, result.StatusCode);
        Assert.False(document.IsActive);
        Assert.Equal(5, document.IdContract);
        Assert.Equal(7, document.IdPanel);
    }

    [Fact]
    public async Task ArchiveAsync_AlreadyArchivedDocumentIsIdempotent()
    {
        var document = CreateDocument(12, 5, null);
        document.Status = "Archived";
        document.IsActive = false;
        var originalUpdatedAt = document.UpdatedAt;
        var repository = new FakeDocumentRepository(document);
        var service = CreateService(repository, accessibleContractId: 5);

        var result = await service.ArchiveAsync(document.IdDocument);

        Assert.True(result.Succeeded);
        Assert.Equal(StatusCodes.Status204NoContent, result.StatusCode);
        Assert.Equal(originalUpdatedAt, document.UpdatedAt);
        Assert.Equal(0, repository.UpdateCount);
    }

    [Fact]
    public async Task ArchiveAsync_InaccessibleContractReturnsNotFound()
    {
        var document = CreateDocument(13, 99, null);
        var repository = new FakeDocumentRepository(document);
        var service = CreateService(repository, accessibleContractId: 5);

        var result = await service.ArchiveAsync(document.IdDocument);

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
        Assert.True(document.IsActive);
        Assert.Equal("Active", document.Status);
    }

    private static DocumentAttachment CreateDocument(int id, int contractId, int? panelId)
    {
        return new DocumentAttachment
        {
            IdDocument = id,
            IdContract = contractId,
            IdPanel = panelId,
            DocumentType = panelId.HasValue ? "PlacementAttachment" : "ContractAttachment",
            DocumentCategory = panelId.HasValue ? "Placement" : "Contract",
            OriginalFileName = "generated.pdf",
            FileName = "generated.pdf",
            FilePath = $"contracts/{contractId}/generated.pdf",
            MimeType = "application/pdf",
            Status = "Active",
            IsActive = true,
            CreatedBy = "user-1",
            CreatedAt = DateTime.UtcNow
        };
    }

    private static DocumentAttachmentService CreateService(
        FakeDocumentRepository documentRepository,
        int accessibleContractId,
        int? accessiblePanelId = null)
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [new SecurityClaim(ClaimTypes.NameIdentifier, "user-1")], "Test"))
            }
        };

        return new DocumentAttachmentService(
            documentRepository,
            new FakeContractRepository(accessibleContractId),
            new FakePlacementRepository(accessibleContractId, accessiblePanelId),
            new FakeStorageService(),
            httpContextAccessor,
            NullLogger<DocumentAttachmentService>.Instance);
    }

    private sealed class FakeDocumentRepository : IDocumentAttachmentRepository
    {
        private readonly DocumentAttachment _document;

        public FakeDocumentRepository(DocumentAttachment document) => _document = document;

        public int UpdateCount { get; private set; }

        public IQueryable<DocumentAttachment> Query() => new[] { _document }.AsQueryable();

        public Task<DocumentAttachment?> GetByIdAsync(int documentId, CancellationToken cancellationToken = default) =>
            Task.FromResult<DocumentAttachment?>(_document.IdDocument == documentId ? _document : null);

        public Task AddAsync(DocumentAttachment document, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Update(DocumentAttachment document) => UpdateCount++;

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }

    private sealed class FakeContractRepository : IContractRepository
    {
        private readonly int _accessibleContractId;

        public FakeContractRepository(int accessibleContractId) => _accessibleContractId = accessibleContractId;

        public Task<Contract?> GetAccessibleContractByIdAsync(int contractId, string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Contract?>(contractId == _accessibleContractId ? new Contract { IdContract = contractId } : null);

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> UmrExistsAsync(string umr, int? excludedContractId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> AllocateUmrSequenceAsync(int inceptionYear, string contractTypeCode, string mainCoverCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddAsync(Contract contract, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddFacultativeDetailsAsync(ContractFac contractFac, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Update(Contract contract) => throw new NotSupportedException();
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Contract?> GetFacultativeContractByIdAsync(int contractId, bool asNoTracking = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task MarkDraftReadyForPlacementAsync(int contractId, string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SlipTemplate>> ListActiveCurrentSlipTemplatesForContractAsync(int contractId, string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(IReadOnlyList<ContractListItemDto> Items, int TotalCount)> ListAsync(ContractListQueryDto query, string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ContractDashboardData> GetDashboardDataAsync(string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakePlacementRepository : IContractPlacementRepository
    {
        private readonly int _contractId;
        private readonly int? _panelId;

        public FakePlacementRepository(int contractId, int? panelId)
        {
            _contractId = contractId;
            _panelId = panelId;
        }

        public Task<ContractPanel?> GetPanelAsync(int contractId, int panelId, string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ContractPanel?>(contractId == _contractId && panelId == _panelId
                ? new ContractPanel { IdContract = contractId, IdPanel = panelId }
                : null);

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Contract?> GetContractForPlacementAsync(int contractId, string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ContractPanelResponseDto>> GetPanelsAsync(int contractId, string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> IsActiveReinsurerAsync(int counterpartyId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ContractHasReinsurerPanelAsync(int contractId, int counterpartyId, int? excludedPanelId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddPanelAsync(ContractPanel panel, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeStorageService : IDocumentStorageService
    {
        public Task<StoredDocument> SaveAsync(string scope, int ownerId, DocumentUploadRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string? relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream?> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
