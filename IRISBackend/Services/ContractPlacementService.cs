using IRISBackend.DTOs;
using IRISBackend.Models;
using IRISBackend.Repositories;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;

namespace IRISBackend.Services
{
    public sealed class ContractPlacementService : IContractPlacementService
    {
        private const string ReinsurerRole = "Reinsurer";

        private readonly IContractPlacementRepository _placementRepository;
        private readonly IContractReadService _contractReadService;
        private readonly IUserService _userService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IRISBackendContext? _db;

        public ContractPlacementService(
            IContractPlacementRepository placementRepository,
            IContractReadService contractReadService,
            IUserService userService,
            IHttpContextAccessor httpContextAccessor,
            IRISBackendContext? db = null)
        {
            _placementRepository = placementRepository;
            _contractReadService = contractReadService;
            _userService = userService;
            _httpContextAccessor = httpContextAccessor;
            _db = db;
        }

        public async Task<ServiceResult<IReadOnlyList<ContractPanelResponseDto>>> GetPanelsAsync(
            int contractId,
            CancellationToken cancellationToken = default)
        {
            var userResult = await GetCurrentContractReaderAsync();
            if (!userResult.Succeeded)
            {
                return ServiceResult<IReadOnlyList<ContractPanelResponseDto>>.Failure(
                    userResult.Error?.Message ?? "Unable to resolve the current user.",
                    userResult.Error?.Errors,
                    userResult.StatusCode);
            }

            var includeAllContracts = FrontOfficerAccessRules.IsBackOfficer(userResult.Value!);
            var contract = await _placementRepository.GetContractForPlacementAsync(
                contractId,
                userResult.Value!.Id,
                includeAllContracts,
                cancellationToken);
            if (contract is null)
            {
                return ServiceResult<IReadOnlyList<ContractPanelResponseDto>>.Failure(
                    "Contract was not found.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var panels = await _placementRepository.GetPanelsAsync(
                contractId,
                userResult.Value.Id,
                includeAllContracts,
                cancellationToken);
            var availableShare = CalculateAvailableShare(contract);
            foreach (var panel in panels)
            {
                panel.AvailableShare = availableShare;
            }
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

            await using IDbContextTransaction transaction = await _placementRepository.BeginSerializableTransactionAsync(cancellationToken);
            try
            {
                var contract = await _placementRepository.GetContractForPlacementAsync(
                    contractId,
                    userResult.Value!.Id,
                    false,
                    cancellationToken);
                if (contract is null)
                {
                    return NotFound();
                }

                var effectiveStatus = ContractConstants.NormalizeStatus(contract.Status);
                if (effectiveStatus != ContractConstants.ReadyForPlacementStatus &&
                    effectiveStatus != ContractConstants.PlacementInProgressStatus)
                {
                    return Conflict($"A reinsurer cannot be added while the contract status is '{effectiveStatus}'.");
                }

                var validation = await ValidatePanelRequestAsync(contract, request, null, cancellationToken);
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

                var hadExistingPanels = contract.ContractPanels.Count > 0;
                await _placementRepository.AddPanelAsync(panel, cancellationToken);

                if (effectiveStatus == ContractConstants.ReadyForPlacementStatus && !hadExistingPanels)
                {
                    contract.Status = ContractConstants.PlacementInProgressStatus;
                    contract.UpdatedAt = DateTime.UtcNow;
                    contract.UpdatedBy = userResult.Value.Id;
                }

                await _placementRepository.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }

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

            var effectiveStatus = ContractConstants.NormalizeStatus(panel.IdContractNavigation.Status);
            if (effectiveStatus != ContractConstants.PlacementInProgressStatus)
            {
                return ServiceResult<ContractOverviewDto>.Failure(
                    $"A reinsurer placement can only be edited while the contract status is '{ContractConstants.PlacementInProgressStatus}'.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            var validation = await ValidatePanelRequestAsync(panel.IdContractNavigation, request, panelId, cancellationToken);
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

            var effectiveStatus = ContractConstants.NormalizeStatus(panel.IdContractNavigation.Status);
            if (effectiveStatus != ContractConstants.PlacementInProgressStatus)
            {
                return ServiceResult<ContractPanelResponseDto>.Failure(
                    $"A reinsurer placement can only be confirmed while the contract status is '{ContractConstants.PlacementInProgressStatus}'.",
                    statusCode: StatusCodes.Status409Conflict);
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

            var contract = await _placementRepository.GetContractForPlacementAsync(
                contractId,
                userResult.Value!.Id,
                false,
                cancellationToken);
            if (contract is null)
            {
                return NotFound();
            }

            var effectiveStatus = ContractConstants.NormalizeStatus(contract.Status);
            if (effectiveStatus != ContractConstants.PlacementInProgressStatus)
            {
                return ServiceResult<ContractOverviewDto>.Failure(
                    $"Binding can only start while the contract status is '{ContractConstants.PlacementInProgressStatus}'.",
                    statusCode: StatusCodes.Status409Conflict);
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

            await using var transaction = _db is null
                ? null
                : await _db.Database.BeginTransactionAsync(cancellationToken);
            contract.Status = ContractConstants.BoundStatus;
            contract.UpdatedAt = DateTime.UtcNow;
            contract.UpdatedBy = userResult.Value.Id;

            foreach (var panel in contract.ContractPanels)
            {
                if (!string.Equals(panel.Status?.Trim(), ContractConstants.ConfirmedPanelStatus, StringComparison.OrdinalIgnoreCase))
                {
                    panel.Status = ContractConstants.NotTakenUpPanelStatus;
                }
            }

            await _placementRepository.SaveChangesAsync(cancellationToken);
            if (_db is not null)
            {
                await NotifyBackOfficerContractReadyForDebitNoteAsync(contract, userResult.Value.Id, cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
                await transaction!.CommitAsync(cancellationToken);
            }
            return await _contractReadService.GetOverviewAsync(contractId, cancellationToken);
        }

        private async Task NotifyBackOfficerContractReadyForDebitNoteAsync(Contract contract, string createdBy, CancellationToken cancellationToken)
        {
            if (_db is null) return;
            var exists = await _db.Notifications.AnyAsync(item =>
                item.NotificationType == "ContractReadyForDebitNote" &&
                item.EntityType == "CONTRACT" &&
                item.EntityId == contract.IdContract, cancellationToken);
            if (exists) return;
            var recipients = await _db.AspNetUsers
                .Where(user => user.Position != null && user.Position.Trim().ToLower() == "back officer")
                .Select(user => user.Id)
                .ToListAsync(cancellationToken);
            foreach (var recipient in recipients)
            {
                _db.Notifications.Add(new Notification
                {
                    RecipientUserId = recipient,
                    RecipientRole = "Back Officer",
                    NotificationType = "ContractReadyForDebitNote",
                    Title = "Contract ready for debit note",
                    Message = $"Contract {contract.Umr} - {contract.Name} is bound and ready for its debit note.",
                    EntityType = "CONTRACT",
                    EntityId = contract.IdContract,
                    TargetRoute = $"/back-office/contracts/{contract.IdContract}/overview",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = createdBy
                });
            }
        }

        public async Task<ServiceResult<ContractOverviewDto>> CancelContractAsync(
            int contractId,
            CancellationToken cancellationToken = default)
        {
            var userResult = await GetCurrentFrontOfficerAsync();
            if (!userResult.Succeeded)
            {
                return MapFailure(userResult);
            }

            var contract = await _placementRepository.GetContractForPlacementAsync(
                contractId,
                userResult.Value!.Id,
                false,
                cancellationToken);
            if (contract is null)
            {
                return NotFound();
            }

            var effectiveStatus = ContractConstants.NormalizeStatus(contract.Status);
            if (effectiveStatus is ContractConstants.BoundStatus or
                ContractConstants.ExpiredStatus or
                ContractConstants.CancelledStatus)
            {
                return ServiceResult<ContractOverviewDto>.Failure(
                    $"A contract cannot be cancelled from status '{effectiveStatus}'.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            contract.Status = ContractConstants.CancelledStatus;
            contract.UpdatedAt = DateTime.UtcNow;
            contract.UpdatedBy = userResult.Value.Id;
            await _placementRepository.SaveChangesAsync(cancellationToken);

            return await _contractReadService.GetOverviewAsync(contractId, cancellationToken);
        }

        private async Task<ServiceResult<ContractOverviewDto>> ValidatePanelRequestAsync(
            Contract contract,
            ContractPanelRequestDto request,
            int? excludedPanelId,
            CancellationToken cancellationToken)
        {
            var errors = new List<string>();

            if (!await _placementRepository.IsActiveReinsurerAsync(request.IdCounterparty, cancellationToken))
            {
                errors.Add("The selected counterparty does not exist, is inactive, or is not a reinsurer.");
            }

            if (await _placementRepository.ContractHasReinsurerPanelAsync(contract.IdContract, request.IdCounterparty, excludedPanelId, cancellationToken))
            {
                errors.Add("This reinsurer is already assigned to the contract panel.");
            }

            if (request.SignedSharePct.HasValue && request.WrittenSharePct.HasValue &&
                request.SignedSharePct.Value > request.WrittenSharePct.Value)
            {
                errors.Add("signedSharePct cannot exceed writtenSharePct.");
            }

            var availableShare = CalculateAvailableShare(contract, excludedPanelId);
            if (request.SignedSharePct.HasValue && request.SignedSharePct.Value > availableShare)
            {
                errors.Add($"signedSharePct cannot exceed the available contract share of {availableShare}.");
            }

            var calculation = ContractPanelFinancialCalculator.Calculate(request);
            if (calculation.NetPremium < 0m)
            {
                errors.Add("Calculated netPremium cannot be negative.");
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

        private async Task<ServiceResult<UserProfileResponseDto>> GetCurrentContractReaderAsync()
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

            return FrontOfficerAccessRules.IsFrontOrBackOfficer(userResult.Value)
                ? userResult
                : ServiceResult<UserProfileResponseDto>.Failure(
                    "Only Front Officers and Back Officers can access contract placement data.",
                    statusCode: StatusCodes.Status403Forbidden);
        }

        private static void ApplyPanelValues(ContractPanel panel, ContractPanelRequestDto request)
        {
            panel.WrittenSharePct = request.WrittenSharePct;
            panel.SignedSharePct = request.SignedSharePct;
            ContractPanelFinancialCalculator.Apply(
                panel,
                ContractPanelFinancialCalculator.Calculate(request));
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

        private static decimal CalculateAvailableShare(Contract contract, int? excludedPanelId = null)
        {
            var signedShare = contract.SignedSharePctCedent ?? 100m;
            var usedShare = contract.ContractPanels
                .Where(panel => (!excludedPanelId.HasValue || panel.IdPanel != excludedPanelId.Value) &&
                    !IsExcludedFromAvailableShare(panel.Status))
                .Sum(panel => panel.SignedSharePct ?? 0m);

            return Math.Max(0m, signedShare - usedShare);
        }

        private static bool IsExcludedFromAvailableShare(string? status)
        {
            var normalized = status?.Trim();
            return string.Equals(normalized, ContractConstants.CancelledPanelStatus, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, ContractConstants.NotTakenUpPanelStatus, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeStatus(string? status)
        {
            return ContractConstants.NormalizeStatus(status);
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

        private static ServiceResult<ContractOverviewDto> Conflict(string message)
        {
            return ServiceResult<ContractOverviewDto>.Failure(
                message,
                statusCode: StatusCodes.Status409Conflict);
        }
    }
}
