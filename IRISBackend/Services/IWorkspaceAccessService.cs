using IRISBackend.DTOs;
using System.Security.Claims;

namespace IRISBackend.Services
{
    public interface IWorkspaceAccessService
    {
        Task<ServiceResult<WorkspaceAccessResponseDto>> GetCurrentWorkspaceAsync(ClaimsPrincipal principal);
    }
}
