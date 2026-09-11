using IRISBackend.DTOs;

namespace IRISBackend.Services;

public interface IDocumentAttachmentService
{
    Task<ServiceResult<IReadOnlyList<DocumentAttachmentResponseDto>>> ListContractDocumentsAsync(
        int contractId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<DocumentAttachmentResponseDto>> UploadContractDocumentAsync(
        int contractId,
        DocumentUploadRequestDto request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<DocumentAttachmentResponseDto>>> ListPanelDocumentsAsync(
        int contractId,
        int panelId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<DocumentAttachmentResponseDto>> UploadPanelDocumentAsync(
        int contractId,
        int panelId,
        DocumentUploadRequestDto request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<DocumentDownload>> DownloadAsync(
        int documentId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<bool>> ArchiveAsync(
        int documentId,
        CancellationToken cancellationToken = default);
}
