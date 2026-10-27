using Microsoft.EntityFrameworkCore;

namespace IRISBackend.Models;

public partial class IRISBackendContext
{
    public virtual DbSet<InvoiceCreditApplication> InvoiceCreditApplications { get; set; }
    public virtual DbSet<AvoirRefundSettlement> AvoirRefundSettlements { get; set; }
}
