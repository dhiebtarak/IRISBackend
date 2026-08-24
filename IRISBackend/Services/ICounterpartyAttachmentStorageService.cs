using IRISBackend.DTOs;

namespace IRISBackend.Services
{
    public interface ICounterpartyAttachmentStorageService
    {
        Task<StoredCounterpartyAttachment> SaveAsync(int counterpartyId, AttachmentUploadDto attachment, CancellationToken cancellationToken = default);

        Task DeleteAsync(string? relativePath, CancellationToken cancellationToken = default);
    }
}
