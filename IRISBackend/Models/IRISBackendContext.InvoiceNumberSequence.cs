using Microsoft.EntityFrameworkCore;

namespace IRISBackend.Models;

public partial class IRISBackendContext
{
    public virtual DbSet<InvoiceNumberSequence> InvoiceNumberSequences { get; set; }
}
