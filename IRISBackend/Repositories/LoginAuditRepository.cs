using IRISBackend.Models;

namespace IRISBackend.Repositories
{
    public class LoginAuditRepository : GenericRepository<UserLoginAudit>, ILoginAuditRepository
    {
        public LoginAuditRepository(IRISBackendContext dbContext) : base(dbContext)
        {
        }
    }
}
