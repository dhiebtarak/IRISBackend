using IRISBackend.DTOs;

namespace IRISBackend.Services;

public interface IDocumentStorageService
{
    Task<StoredDocument> SaveAsync(
        string scope,
        int ownerId,
        DocumentUploadRequestDto request,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string? relativePath, CancellationToken cancellationToken = default);

    Task<Stream?> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default);
}
