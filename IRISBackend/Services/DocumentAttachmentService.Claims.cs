using IRISBackend.DTOs;
using IRISBackend.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace IRISBackend.Services;

public sealed partial class DocumentAttachmentService
{
    private async Task<ServiceResult<object>> EnsureClaimAccessAsync(int claimId, CancellationToken ct)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(GetCurrentUserId()))
            return ServiceResult<object>.Failure("Authentication is required.", statusCode: 401);
        var managesClaims = !user.IsInRole(IdentityRoles.Admin)
            && user.HasClaim(ModuleAccessCatalog.ClaimType, ModuleAccessCatalog.Claims);
        if (!managesClaims && !FinancialAccessRules.IsBackOfficer(user))
            return ServiceResult<object>.Failure("Claims access is required.", statusCode: 403);
        // Match the claim list: Back Office sees claims handed over as Paid loss or already issued.
        var exists = await _db.Claims.AsNoTracking().AnyAsync(c => c.ClaimRef == claimId
            && (managesClaims || c.Status == "PaidLoss" || c.CollectionIssuedAt != null), ct);
        return exists ? ServiceResult<object>.Success(new object())
            : ServiceResult<object>.Failure("Claim was not found.", statusCode: 404);
    }

    public async Task<ServiceResult<IReadOnlyList<DocumentAttachmentResponseDto>>> ListClaimDocumentsAsync(int claimId, CancellationToken cancellationToken = default)
    {
        var access = await EnsureClaimAccessAsync(claimId, cancellationToken);
        if (!access.Succeeded) return Failure<IReadOnlyList<DocumentAttachmentResponseDto>>(access);
        var documents = await _documentRepository.Query().AsNoTracking().Include(d => d.CreatedByNavigation)
            .Where(d => d.IdClaim == claimId && d.IsActive && d.Status != "Archived")
            .OrderByDescending(d => d.CreatedAt).ToListAsync(cancellationToken);
        return ServiceResult<IReadOnlyList<DocumentAttachmentResponseDto>>.Success(documents.Select(d => MapClaimDocument(d, claimId)).ToArray());
    }

    public async Task<ServiceResult<DocumentAttachmentResponseDto>> UploadClaimDocumentAsync(int claimId, DocumentUploadRequestDto request, CancellationToken cancellationToken = default)
    {
        var access = await EnsureClaimAccessAsync(claimId, cancellationToken);
        if (!access.Succeeded) return Failure<DocumentAttachmentResponseDto>(access);
        StoredDocument? stored = null;
        try
        {
            stored = await _storageService.SaveAsync("claims", claimId, request, cancellationToken);
            await using var transaction = await _documentRepository.BeginTransactionAsync(cancellationToken);
            var document = new DocumentAttachment
            {
                IdClaim = claimId,
                DocumentType = "ClaimAttachment", DocumentCategory = "Claim",
                Title = stored.OriginalFileName, FileName = stored.StoredFileName,
                OriginalFileName = stored.OriginalFileName, FileExtension = stored.FileExtension,
                MimeType = stored.MimeType, FilePath = stored.RelativePath, FileSize = stored.FileSize,
                StorageProvider = LocalStorageProvider, ConfidentialityLevel = "Internal",
                Status = ActiveStatus, IsActive = true, CreatedAt = DateTime.UtcNow, CreatedBy = GetCurrentUserId()!
            };
            await _documentRepository.AddAsync(document, cancellationToken);
            await _documentRepository.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ServiceResult<DocumentAttachmentResponseDto>.Success(MapClaimDocument(document, claimId), 201);
        }
        catch (InvalidOperationException exception)
        {
            if (stored is not null) await _storageService.DeleteAsync(stored.RelativePath, cancellationToken);
            return ServiceResult<DocumentAttachmentResponseDto>.Failure(exception.Message);
        }
        catch (Exception exception)
        {
            if (stored is not null) await _storageService.DeleteAsync(stored.RelativePath, cancellationToken);
            _logger.LogError(exception, "Failed to save document for claim {ClaimId}.", claimId);
            return ServiceResult<DocumentAttachmentResponseDto>.Failure("The document could not be saved.", statusCode: 500);
        }
    }

    public async Task<ServiceResult<DocumentDownload>> DownloadClaimDocumentAsync(int claimId, int documentId, CancellationToken cancellationToken = default)
    {
        var access = await EnsureClaimAccessAsync(claimId, cancellationToken);
        if (!access.Succeeded) return Failure<DocumentDownload>(access);
        var document = await _documentRepository.GetByIdAsync(documentId, cancellationToken);
        if (document is null || document.IdClaim != claimId || !IsListed(document)) return NotFound<DocumentDownload>();
        var stream = await _storageService.OpenReadAsync(document.FilePath, cancellationToken);
        return stream is null ? NotFound<DocumentDownload>() : ServiceResult<DocumentDownload>.Success(new()
        { Content = stream, FileName = document.OriginalFileName, MimeType = document.MimeType });
    }

    public async Task<ServiceResult<bool>> ArchiveClaimDocumentAsync(int claimId, int documentId, CancellationToken cancellationToken = default)
    {
        var access = await EnsureClaimAccessAsync(claimId, cancellationToken);
        if (!access.Succeeded) return Failure<bool>(access);
        var document = await _db.DocumentAttachments.SingleOrDefaultAsync(d => d.IdDocument == documentId, cancellationToken);
        if (document is null || document.IdClaim != claimId) return NotFound<bool>();
        if (!IsListed(document)) return ServiceResult<bool>.Success(true, 204);
        document.Status = "Archived";
        document.IsActive = false;
        document.UpdatedAt = DateTime.UtcNow;
        document.UpdatedBy = GetCurrentUserId();
        // Update the attachment only; do not attach/update the uploader's user navigation graph.
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return ServiceResult<bool>.Success(true, 204);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to archive claim document {DocumentId}.", documentId);
            return ServiceResult<bool>.Failure("The document could not be archived.", statusCode: 500);
        }
    }

    private static DocumentAttachmentResponseDto MapClaimDocument(DocumentAttachment document, int claimId)
    {
        var response = MapDocument(document);
        response.DownloadUrl = $"/api/claims/{claimId}/attachments/{document.IdDocument}/download";
        return response;
    }
}
