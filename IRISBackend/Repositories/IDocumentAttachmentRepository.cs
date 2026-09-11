using IRISBackend.Models;
using Microsoft.EntityFrameworkCore.Storage;

namespace IRISBackend.Repositories;

public interface IDocumentAttachmentRepository
{
    IQueryable<DocumentAttachment> Query();

    Task<DocumentAttachment?> GetByIdAsync(int documentId, CancellationToken cancellationToken = default);

    Task AddAsync(DocumentAttachment document, CancellationToken cancellationToken = default);

    void Update(DocumentAttachment document);

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
