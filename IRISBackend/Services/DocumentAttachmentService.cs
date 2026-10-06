using IRISBackend.DTOs;
using IRISBackend.Models;
using IRISBackend.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace IRISBackend.Services;

public sealed class DocumentAttachmentService : IDocumentAttachmentService
{
    private const string ContractDocumentType = "ContractAttachment";
    private const string PanelDocumentType = "PlacementAttachment";
    private const string ContractCategory = "Contract";
    private const string PlacementCategory = "Placement";
    private const string ActiveStatus = "Active";
    private const string LocalStorageProvider = "LocalFileSystem";

    private readonly IDocumentAttachmentRepository _documentRepository;
    private readonly IContractRepository _contractRepository;
    private readonly IContractPlacementRepository _placementRepository;
    private readonly IDocumentStorageService _storageService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<DocumentAttachmentService> _logger;

    public DocumentAttachmentService(
        IDocumentAttachmentRepository documentRepository,
        IContractRepository contractRepository,
        IContractPlacementRepository placementRepository,
        IDocumentStorageService storageService,
        IHttpContextAccessor httpContextAccessor,
        ILogger<DocumentAttachmentService> logger)
    {
        _documentRepository = documentRepository;
        _contractRepository = contractRepository;
        _placementRepository = placementRepository;
        _storageService = storageService;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<ServiceResult<IReadOnlyList<DocumentAttachmentResponseDto>>> ListContractDocumentsAsync(
        int contractId,
        CancellationToken cancellationToken = default)
    {
        var access = await EnsureContractAccessAsync(contractId, cancellationToken);
        if (!access.Succeeded) return ServiceResult<IReadOnlyList<DocumentAttachmentResponseDto>>.Failure(
            access.Error!.Message, access.Error.Errors, access.StatusCode);

        var documents = await _documentRepository.Query()
            .AsNoTracking()
            .Include(document => document.CreatedByNavigation)
            .Where(document => document.IdContract == contractId && document.IdPanel == null && document.IsActive && document.Status != "Archived")
            .OrderByDescending(document => document.CreatedAt)
            .ToListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<DocumentAttachmentResponseDto>>.Success(MapDocuments(documents));
    }

    public async Task<ServiceResult<DocumentAttachmentResponseDto>> UploadContractDocumentAsync(
        int contractId,
        DocumentUploadRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var access = await EnsureContractAccessAsync(contractId, cancellationToken);
        if (!access.Succeeded) return Failure<DocumentAttachmentResponseDto>(access);

        return await UploadAsync(contractId, null, request, cancellationToken);
    }

    public async Task<ServiceResult<IReadOnlyList<DocumentAttachmentResponseDto>>> ListPanelDocumentsAsync(
        int contractId,
        int panelId,
        CancellationToken cancellationToken = default)
    {
        var panel = await GetAccessiblePanelAsync(contractId, panelId, cancellationToken);
        if (panel is null) return NotFound<IReadOnlyList<DocumentAttachmentResponseDto>>();

        var documents = await _documentRepository.Query()
            .AsNoTracking()
            .Include(document => document.CreatedByNavigation)
            .Where(document => document.IdContract == contractId && document.IdPanel == panelId && document.IsActive && document.Status != "Archived")
            .OrderByDescending(document => document.CreatedAt)
            .ToListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<DocumentAttachmentResponseDto>>.Success(MapDocuments(documents));
    }

    public async Task<ServiceResult<DocumentAttachmentResponseDto>> UploadPanelDocumentAsync(
        int contractId,
        int panelId,
        DocumentUploadRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var panel = await GetAccessiblePanelAsync(contractId, panelId, cancellationToken);
        if (panel is null) return NotFound<DocumentAttachmentResponseDto>();

        return await UploadAsync(contractId, panelId, request, cancellationToken);
    }

    public async Task<ServiceResult<DocumentDownload>> DownloadAsync(
        int documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetByIdAsync(documentId, cancellationToken);
        if (document is null || !document.IsActive || IsArchived(document.Status) || !document.IdContract.HasValue)
        {
            return NotFound<DocumentDownload>();
        }

        var access = await EnsureContractAccessAsync(document.IdContract.Value, cancellationToken);
        if (!access.Succeeded) return Failure<DocumentDownload>(access);

        if (document.IdPanel.HasValue)
        {
            var panel = await GetAccessiblePanelAsync(document.IdContract.Value, document.IdPanel.Value, cancellationToken);
            if (panel is null) return NotFound<DocumentDownload>();
        }

        var stream = await _storageService.OpenReadAsync(document.FilePath, cancellationToken);
        if (stream is null) return NotFound<DocumentDownload>();

        return ServiceResult<DocumentDownload>.Success(new DocumentDownload
        {
            Content = stream,
            FileName = document.OriginalFileName,
            MimeType = document.MimeType
        });
    }

    public async Task<ServiceResult<bool>> ArchiveAsync(
        int documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetByIdAsync(documentId, cancellationToken);
        if (document is null || !document.IdContract.HasValue)
        {
            return NotFound<bool>();
        }

        var access = await EnsureContractAccessAsync(document.IdContract.Value, cancellationToken);
        if (!access.Succeeded) return Failure<bool>(access);

        if (document.IdPanel.HasValue)
        {
            var panel = await GetAccessiblePanelAsync(document.IdContract.Value, document.IdPanel.Value, cancellationToken);
            if (panel is null) return NotFound<bool>();
        }

        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return ServiceResult<bool>.Failure(
                "Authenticated user could not be resolved.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!document.IsActive || IsArchived(document.Status))
        {
            return ServiceResult<bool>.Success(true, StatusCodes.Status204NoContent);
        }

        document.Status = "Archived";
        document.IsActive = false;
        document.UpdatedAt = DateTime.UtcNow;
        document.UpdatedBy = userId;

        try
        {
            _documentRepository.Update(document);
            await _documentRepository.SaveChangesAsync(cancellationToken);
            return ServiceResult<bool>.Success(true, StatusCodes.Status204NoContent);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to archive document {DocumentId}.", documentId);
            return ServiceResult<bool>.Failure(
                "The document could not be archived.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private async Task<ServiceResult<DocumentAttachmentResponseDto>> UploadAsync(
        int contractId,
        int? panelId,
        DocumentUploadRequestDto request,
        CancellationToken cancellationToken)
    {
        StoredDocument? stored = null;
        try
        {
            stored = await _storageService.SaveAsync(
                panelId.HasValue ? "placements" : "contracts",
                panelId ?? contractId,
                request,
                cancellationToken);

            var userId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                await _storageService.DeleteAsync(stored.RelativePath, cancellationToken);
                return ServiceResult<DocumentAttachmentResponseDto>.Failure("Authenticated user could not be resolved.", statusCode: StatusCodes.Status401Unauthorized);
            }

            await using var transaction = await _documentRepository.BeginTransactionAsync(cancellationToken);
            var document = new DocumentAttachment
            {
                IdContract = contractId,
                IdPanel = panelId,
                DocumentType = panelId.HasValue ? PanelDocumentType : ContractDocumentType,
                DocumentCategory = panelId.HasValue ? PlacementCategory : ContractCategory,
                Title = stored.OriginalFileName,
                Description = null,
                FileName = stored.StoredFileName,
                OriginalFileName = stored.OriginalFileName,
                FileExtension = stored.FileExtension,
                MimeType = stored.MimeType,
                StorageProvider = LocalStorageProvider,
                FilePath = stored.RelativePath,
                ConfidentialityLevel = "Internal",
                Status = ActiveStatus,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userId,
                FileSize = stored.FileSize
            };

            await _documentRepository.AddAsync(document, cancellationToken);
            await _documentRepository.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return ServiceResult<DocumentAttachmentResponseDto>.Success(MapDocument(document), StatusCodes.Status201Created);
        }
        catch (InvalidOperationException exception)
        {
            if (stored is not null) await _storageService.DeleteAsync(stored.RelativePath, cancellationToken);
            return ServiceResult<DocumentAttachmentResponseDto>.Failure(exception.Message);
        }
        catch (Exception exception)
        {
            if (stored is not null) await _storageService.DeleteAsync(stored.RelativePath, cancellationToken);
            _logger.LogError(exception, "Failed to save document attachment for contract {ContractId}, panel {PanelId}.", contractId, panelId);
            return ServiceResult<DocumentAttachmentResponseDto>.Failure("The document could not be saved.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private async Task<ServiceResult<object>> EnsureContractAccessAsync(int contractId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return ServiceResult<object>.Failure("Authenticated user could not be resolved.", statusCode: StatusCodes.Status401Unauthorized);
        }

        var contract = await _contractRepository.GetAccessibleContractByIdAsync(
            contractId,
            userId,
            includeAllContracts: FinancialAccessRules.IsBackOfficer(_httpContextAccessor.HttpContext?.User ?? new System.Security.Claims.ClaimsPrincipal()),
            cancellationToken);
        return contract is null
            ? ServiceResult<object>.Failure("Contract was not found.", statusCode: StatusCodes.Status404NotFound)
            : ServiceResult<object>.Success(new object());
    }

    private async Task<ContractPanel?> GetAccessiblePanelAsync(int contractId, int panelId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        return string.IsNullOrWhiteSpace(userId)
            ? null
            : await _placementRepository.GetPanelAsync(contractId, panelId, userId, cancellationToken);
    }

    private string? GetCurrentUserId()
    {
        return _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    private static IReadOnlyList<DocumentAttachmentResponseDto> MapDocuments(IEnumerable<DocumentAttachment> documents)
    {
        return documents.Select(MapDocument).ToList();
    }

    private static DocumentAttachmentResponseDto MapDocument(DocumentAttachment document)
    {
        return new DocumentAttachmentResponseDto
        {
            IdDocument = document.IdDocument,
            FileName = document.OriginalFileName,
            MimeType = document.MimeType,
            FileSize = document.FileSize ?? 0,
            DocumentType = document.DocumentType,
            CreatedAt = document.CreatedAt,
            CreatedByName = document.CreatedByNavigation?.UserName ?? document.CreatedBy,
            DownloadUrl = $"/api/documents/{document.IdDocument}/download"
        };
    }

    private static bool IsListed(DocumentAttachment document)
    {
        return document.IsActive && !IsArchived(document.Status);
    }

    private static bool IsArchived(string? status)
    {
        return string.Equals(status, "Archived", StringComparison.OrdinalIgnoreCase);
    }

    private static ServiceResult<T> NotFound<T>() => ServiceResult<T>.Failure("Document or parent resource was not found.", statusCode: StatusCodes.Status404NotFound);

    private static ServiceResult<T> Failure<T>(ServiceResult<object> result) =>
        ServiceResult<T>.Failure(result.Error!.Message, result.Error.Errors, result.StatusCode);
}
