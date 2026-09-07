using IRISBackend.DTOs;
using IRISBackend.Models;
using IRISBackend.Repositories;

namespace IRISBackend.Services
{
    public sealed class ContractPlacementService : IContractPlacementService
    {
        private const string ReinsurerRole = "Reinsurer";

        private readonly IContractPlacementRepository _placementRepository;
        private readonly IContractReadService _contractReadService;
        private readonly IUserService _userService;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ContractPlacementService(
            IContractPlacementRepository placementRepository,
            IContractReadService contractReadService,
            IUserService userService,
            IHttpContextAccessor httpContextAccessor)
        {
            _placementRepository = placementRepository;
            _contractReadService = contractReadService;
            _userService = userService;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<ServiceResult<IReadOnlyList<ContractPanelResponseDto>>> GetPanelsAsync(
            int contractId,
            CancellationToken cancellationToken = default)
        {
            var userResult = await GetCurrentFrontOfficerAsync();
            if (!userResult.Succeeded)
            {
                return ServiceResult<IReadOnlyList<ContractPanelResponseDto>>.Failure(
                    userResult.Error?.Message ?? "Unable to resolve the current user.",
                    userResult.Error?.Errors,
                    userResult.StatusCode);
            }

            var contract = await _placementRepository.GetContractForPlacementAsync(contractId, userResult.Value!.Id, cancellationToken);
            if (contract is null)
            {
                return ServiceResult<IReadOnlyList<ContractPanelResponseDto>>.Failure(
                    "Contract was not found.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var panels = await _placementRepository.GetPanelsAsync(contractId, userResult.Value.Id, cancellationToken);
            return ServiceResult<IReadOnlyList<ContractPanelResponseDto>>.Success(panels);
        }

        public async Task<ServiceResult<ContractOverviewDto>> AddPanelAsync(
            int contractId,
            ContractPanelRequestDto request,
            CancellationToken cancellationToken = default)
        {
            var userResult = await GetCurrentFrontOfficerAsync();
            if (!userResult.Succeeded)
            {
                return MapFailure(userResult);
            }

            var contract = await _placementRepository.GetContractForPlacementAsync(contractId, userResult.Value!.Id, cancellationToken);
            if (contract is null)
            {
                return NotFound();
            }

            var validation = await ValidatePanelRequestAsync(contractId, request, null, cancellationToken);
            if (!validation.Succeeded)
            {
                return validation;
            }

            var panel = new ContractPanel
            {
                IdContract = contractId,
                IdCounterparty = request.IdCounterparty,
                Role = ReinsurerRole,
                Status = NormalizeStatus(request.Status),
                PremiumReference = NormalizeOptionalValue(request.PremiumReference)
            };

            ApplyPanelValues(panel, request);

            if (contract.ContractPanels.Count == 0)
            {
                contract.Status = ContractConstants.PlacementInProgressStatus;
                contract.UpdatedAt = DateTime.UtcNow;
                contract.UpdatedBy = userResult.Value.Id;
            }

            await _placementRepository.AddPanelAsync(panel, cancellationToken);
            await _placementRepository.SaveChangesAsync(cancellationToken);

            return await _contractReadService.GetOverviewAsync(contractId, cancellationToken);
        }

        public async Task<ServiceResult<ContractOverviewDto>> UpdatePanelAsync(
            int contractId,
            int panelId,
            ContractPanelRequestDto request,
            CancellationToken cancellationToken = default)
        {
            var userResult = await GetCurrentFrontOfficerAsync();
            if (!userResult.Succeeded)
            {
                return MapFailure(userResult);
            }

            var panel = await _placementRepository.GetPanelAsync(contractId, panelId, userResult.Value!.Id, cancellationToken);
            if (panel is null)
            {
                return ServiceResult<ContractOverviewDto>.Failure(
                    "Contract panel was not found.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var validation = await ValidatePanelRequestAsync(contractId, request, panelId, cancellationToken);
            if (!validation.Succeeded)
            {
                return validation;
            }

            panel.IdCounterparty = request.IdCounterparty;
            panel.Role = ReinsurerRole;
            panel.Status = NormalizeStatus(request.Status);
            panel.PremiumReference = NormalizeOptionalValue(request.PremiumReference);
            ApplyPanelValues(panel, request);

            await _placementRepository.SaveChangesAsync(cancellationToken);
            return await _contractReadService.GetOverviewAsync(contractId, cancellationToken);
        }

        public async Task<ServiceResult<ContractPanelResponseDto>> ConfirmPanelAsync(
            int contractId,
            int panelId,
            CancellationToken cancellationToken = default)
        {
            var userResult = await GetCurrentFrontOfficerAsync();
            if (!userResult.Succeeded)
            {
                return ServiceResult<ContractPanelResponseDto>.Failure(
                    userResult.Error?.Message ?? "Unable to resolve the current user.",
                    userResult.Error?.Errors,
                    userResult.StatusCode);
            }

            var panel = await _placementRepository.GetPanelAsync(contractId, panelId, userResult.Value!.Id, cancellationToken);
            if (panel is null)
            {
                return ServiceResult<ContractPanelResponseDto>.Failure(
                    "Contract panel was not found.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var errors = ValidatePanelCanBeConfirmed(panel);
            if (errors.Count > 0)
            {
                return ServiceResult<ContractPanelResponseDto>.Failure(
                    "Validation failed.",
                    errors,
                    StatusCodes.Status400BadRequest);
            }

            panel.Status = ContractConstants.ConfirmedPanelStatus;

            await _placementRepository.SaveChangesAsync(cancellationToken);
            return ServiceResult<ContractPanelResponseDto>.Success(ContractPanelDtoMapper.Map(panel));
        }

        public async Task<ServiceResult<ContractOverviewDto>> StartBindingAsync(
            int contractId,
            CancellationToken cancellationToken = default)
        {
            var userResult = await GetCurrentFrontOfficerAsync();
            if (!userResult.Succeeded)
            {
                return MapFailure(userResult);
            }

            var contract = await _placementRepository.GetContractForPlacementAsync(contractId, userResult.Value!.Id, cancellationToken);
            if (contract is null)
            {
                return NotFound();
            }

            var confirmedPanels = contract.ContractPanels
                .Where(panel => string.Equals(
                    panel.Status?.Trim(),
                    ContractConstants.ConfirmedPanelStatus,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (confirmedPanels.Length == 0)
            {
                return ServiceResult<ContractOverviewDto>.Failure(
                    "Please confirm at least one reinsurer before starting the binding phase.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            contract.Status = ContractConstants.BindingInProgressStatus;
            contract.UpdatedAt = DateTime.UtcNow;
            contract.UpdatedBy = userResult.Value.Id;

            await _placementRepository.SaveChangesAsync(cancellationToken);
            return await _contractReadService.GetOverviewAsync(contractId, cancellationToken);
        }

        private async Task<ServiceResult<ContractOverviewDto>> ValidatePanelRequestAsync(
            int contractId,
            ContractPanelRequestDto request,
            int? excludedPanelId,
            CancellationToken cancellationToken)
        {
            var errors = new List<string>();

            if (!await _placementRepository.IsActiveReinsurerAsync(request.IdCounterparty, cancellationToken))
            {
                errors.Add("The selected counterparty does not exist, is inactive, or is not a reinsurer.");
            }

            if (await _placementRepository.ContractHasReinsurerPanelAsync(contractId, request.IdCounterparty, excludedPanelId, cancellationToken))
            {
                errors.Add("This reinsurer is already assigned to the contract panel.");
            }

            return errors.Count == 0
                ? ServiceResult<ContractOverviewDto>.Success(new ContractOverviewDto())
                : ServiceResult<ContractOverviewDto>.Failure("Validation failed.", errors, StatusCodes.Status400BadRequest);
        }

        private async Task<ServiceResult<UserProfileResponseDto>> GetCurrentFrontOfficerAsync()
        {
            var principal = _httpContextAccessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true)
            {
                return ServiceResult<UserProfileResponseDto>.Failure(
                    "User identity was not found in the token.",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            var userResult = await _userService.GetCurrentUserAsync(principal);
            if (!userResult.Succeeded || userResult.Value is null)
            {
                return userResult;
            }

            return FrontOfficerAccessRules.IsFrontOfficer(userResult.Value)
                ? userResult
                : ServiceResult<UserProfileResponseDto>.Failure(
                    "Only Front Officers can manage contract placement data.",
                    statusCode: StatusCodes.Status403Forbidden);
        }

        private static void ApplyPanelValues(ContractPanel panel, ContractPanelRequestDto request)
        {
            panel.WrittenSharePct = request.WrittenSharePct;
            panel.SignedSharePct = request.SignedSharePct;
            panel.Tsi = request.Tsi;
            panel.Deductible = request.Deductible;
            panel.PremiumRatePct = request.PremiumRatePct;
            panel.GrossPremium = request.GrossPremium;
            panel.BrokeragePct = request.BrokeragePct;
            panel.OtherDeductions = request.OtherDeductions;
            panel.NetPremium = CalculateNetPremium(request.GrossPremium, request.BrokeragePct, request.OtherDeductions);
        }

        private static IReadOnlyList<string> ValidatePanelCanBeConfirmed(ContractPanel panel)
        {
            var errors = new List<string>();

            if (panel.IdCounterparty <= 0)
            {
                errors.Add("idCounterparty is required before confirming the reinsurer.");
            }

            if (!panel.WrittenSharePct.HasValue)
            {
                errors.Add("writtenSharePct is required before confirming the reinsurer.");
            }

            if (!panel.SignedSharePct.HasValue)
            {
                errors.Add("signedSharePct is required before confirming the reinsurer.");
            }

            if (!panel.GrossPremium.HasValue)
            {
                errors.Add("grossPremium is required before confirming the reinsurer.");
            }

            return errors;
        }

        private static decimal? CalculateNetPremium(decimal? grossPremium, decimal? brokeragePct, decimal? otherDeductions)
        {
            if (!grossPremium.HasValue)
            {
                return null;
            }

            var brokerageAmount = grossPremium.Value * (brokeragePct ?? 0m) / 100m;
            return grossPremium.Value - brokerageAmount - (otherDeductions ?? 0m);
        }

        private static string NormalizeStatus(string? status)
        {
            return string.IsNullOrWhiteSpace(status) ? ContractConstants.DraftStatus : status.Trim();
        }

        private static string? NormalizeOptionalValue(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static ServiceResult<ContractOverviewDto> MapFailure(ServiceResult<UserProfileResponseDto> result)
        {
            return ServiceResult<ContractOverviewDto>.Failure(
                result.Error?.Message ?? "Unable to resolve the current user.",
                result.Error?.Errors,
                result.StatusCode);
        }

        private static ServiceResult<ContractOverviewDto> NotFound()
        {
            return ServiceResult<ContractOverviewDto>.Failure(
                "Contract was not found.",
                statusCode: StatusCodes.Status404NotFound);
        }
    }
}
