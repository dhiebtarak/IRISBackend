using IRISBackend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace IRISBackend.Repositories;

public sealed class DocumentAttachmentRepository : IDocumentAttachmentRepository
{
    private readonly IRISBackendContext _dbContext;

    public DocumentAttachmentRepository(IRISBackendContext dbContext)
    {
        _dbContext = dbContext;
    }

    public IQueryable<DocumentAttachment> Query() => _dbContext.DocumentAttachments;

    public Task<DocumentAttachment?> GetByIdAsync(int documentId, CancellationToken cancellationToken = default)
    {
        return _dbContext.DocumentAttachments
            .AsNoTracking()
            .Include(document => document.CreatedByNavigation)
            .SingleOrDefaultAsync(document => document.IdDocument == documentId, cancellationToken);
    }

    public Task AddAsync(DocumentAttachment document, CancellationToken cancellationToken = default)
    {
        return _dbContext.DocumentAttachments.AddAsync(document, cancellationToken).AsTask();
    }

    public void Update(DocumentAttachment document)
    {
        _dbContext.DocumentAttachments.Update(document);
    }

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}
