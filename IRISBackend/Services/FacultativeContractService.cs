using AutoMapper;
using IRISBackend.DTOs;
using IRISBackend.Models;
using IRISBackend.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace IRISBackend.Services
{
    public sealed class FacultativeContractService : IFacultativeContractService
    {
        private readonly IContractRepository _contractRepository;
        private readonly IReferenceDataRepository _referenceDataRepository;
        private readonly IUserService _userService;
        private readonly IMapper _mapper;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public FacultativeContractService(
            IContractRepository contractRepository,
            IReferenceDataRepository referenceDataRepository,
            IUserService userService,
            IMapper mapper,
            IHttpContextAccessor httpContextAccessor)
        {
            _contractRepository = contractRepository;
            _referenceDataRepository = referenceDataRepository;
            _userService = userService;
            _mapper = mapper;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<ServiceResult<FacultativeContractResponseDto>> CreateDraftAsync(
            UpsertFacultativeContractRequest request,
            CancellationToken cancellationToken = default)
        {
            var currentUserResult = await GetCurrentUserAsync(cancellationToken);
            if (!currentUserResult.Succeeded)
            {
                return MapFailure(currentUserResult);
            }

            if (!IsFrontOfficer(currentUserResult.Value!))
            {
                return Forbidden("Only Front Officers can create facultative contract drafts.");
            }

            var normalizedUmr = NormalizeOptionalValue(request.Umr);
            if (normalizedUmr is not null &&
                await _contractRepository.UmrExistsAsync(normalizedUmr, null, cancellationToken))
            {
                return Conflict("A contract with the same UMR already exists.");
            }

            var validationResult = await ValidateBusinessReferencesAsync(request, cancellationToken);
            if (!validationResult.Succeeded)
            {
                return validationResult;
            }

            var now = DateTime.UtcNow;
            var contract = BuildContractEntity(request, currentUserResult.Value!, normalizedUmr, now);
            var contractFac = BuildContractFacEntity(request);

            await using IDbContextTransaction transaction = await _contractRepository.BeginTransactionAsync(cancellationToken);

            try
            {
                await _contractRepository.AddAsync(contract, cancellationToken);
                await _contractRepository.SaveChangesAsync(cancellationToken);

                contractFac.IdContract = contract.IdContract;
                await _contractRepository.AddFacultativeDetailsAsync(contractFac, cancellationToken);
                await _contractRepository.SaveChangesAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsUmrConflict(exception))
            {
                await transaction.RollbackAsync(cancellationToken);
                return Conflict("A contract with the same UMR already exists.");
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }

            var savedContract = await _contractRepository.GetFacultativeContractByIdAsync(contract.IdContract, asNoTracking: true, cancellationToken);
            return ServiceResult<FacultativeContractResponseDto>.Success(
                _mapper.Map<FacultativeContractResponseDto>(savedContract!),
                StatusCodes.Status201Created);
        }

        public async Task<ServiceResult<FacultativeContractResponseDto>> GetByIdAsync(
            int contractId,
            CancellationToken cancellationToken = default)
        {
            var contract = await _contractRepository.GetFacultativeContractByIdAsync(contractId, asNoTracking: true, cancellationToken);
            if (contract is null)
            {
                return NotFound();
            }

            return ServiceResult<FacultativeContractResponseDto>.Success(_mapper.Map<FacultativeContractResponseDto>(contract));
        }

        public async Task<ServiceResult<FacultativeContractResponseDto>> UpdateDraftAsync(
            int contractId,
            UpsertFacultativeContractRequest request,
            CancellationToken cancellationToken = default)
        {
            var currentUserResult = await GetCurrentUserAsync(cancellationToken);
            if (!currentUserResult.Succeeded)
            {
                return MapFailure(currentUserResult);
            }

            if (!IsFrontOfficer(currentUserResult.Value!))
            {
                return Forbidden("Only Front Officers can update facultative contract drafts.");
            }

            var contract = await _contractRepository.GetFacultativeContractByIdAsync(contractId, cancellationToken: cancellationToken);
            if (contract is null)
            {
                return NotFound();
            }

            if (!string.Equals(contract.Status, ContractConstants.DraftStatus, StringComparison.OrdinalIgnoreCase))
            {
                return Conflict("Only DRAFT facultative contracts can be updated.");
            }

            if (!string.Equals(contract.IdUser, currentUserResult.Value!.Id, StringComparison.Ordinal))
            {
                return Forbidden("You do not have access to update another user's draft.");
            }

            var normalizedUmr = NormalizeOptionalValue(request.Umr);
            if (normalizedUmr is not null &&
                await _contractRepository.UmrExistsAsync(normalizedUmr, contractId, cancellationToken))
            {
                return Conflict("A contract with the same UMR already exists.");
            }

            var validationResult = await ValidateBusinessReferencesAsync(request, cancellationToken);
            if (!validationResult.Succeeded)
            {
                return validationResult;
            }

            ApplyRequest(contract, request, currentUserResult.Value!.Id, DateTime.UtcNow, normalizedUmr);

            if (contract.ContractFac is null)
            {
                contract.ContractFac = new ContractFac
                {
                    IdContract = contract.IdContract
                };
            }

            contract.ContractFac.SumInsured = request.SumInsured;
            contract.ContractFac.Deductible = request.Deductible;
            contract.ContractFac.PremiumRate = request.PremiumRate;

            _contractRepository.Update(contract);

            try
            {
                await _contractRepository.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsUmrConflict(exception))
            {
                return Conflict("A contract with the same UMR already exists.");
            }

            var refreshedContract = await _contractRepository.GetFacultativeContractByIdAsync(contractId, asNoTracking: true, cancellationToken);
            return ServiceResult<FacultativeContractResponseDto>.Success(_mapper.Map<FacultativeContractResponseDto>(refreshedContract!));
        }

        private Contract BuildContractEntity(
            UpsertFacultativeContractRequest request,
            UserProfileResponseDto currentUser,
            string? normalizedUmr,
            DateTime now)
        {
            var contract = new Contract
            {
                Umr = normalizedUmr,
                Name = request.Name.Trim(),
                IdCounterparty = request.CounterpartyId,
                IdCurrency = request.CurrencyId,
                ReceptionDate = request.ReceptionDate,
                InceptionDate = request.InceptionDate,
                ExpiryDate = request.ExpiryDate,
                Fees = request.Fees,
                Flag = request.IsHighPriority,
                WrittenSharePctCedent = request.WrittenSharePct,
                SignedSharePctCedent = request.SignedSharePct,
                GrossPremium = request.GrossPremium,
                CedentCommission = request.CedentCommissionPct,
                NetPremiumCedent = CalculateNetPremium(request.GrossPremium, request.CedentCommissionPct),
                CedentPremiumReference = NormalizeOptionalValue(request.CedentPremiumReference),
                IsFronting = request.IsFronting,
                FrontingReference = request.IsFronting ? NormalizeOptionalValue(request.FrontingReference) : null,
                MemoText = NormalizeOptionalValue(request.MemoText),
                TypeOfBusiness = ContractConstants.FacultativeType,
                Status = ContractConstants.DraftStatus,
                CreationDate = DateOnly.FromDateTime(now),
                CreatedAt = now,
                IdUser = currentUser.Id,
                CreatedBy = currentUser.Id,
                IsCurrent = true
            };

            return contract;
        }

        private static ContractFac BuildContractFacEntity(UpsertFacultativeContractRequest request)
        {
            return new ContractFac
            {
                SumInsured = request.SumInsured,
                Deductible = request.Deductible,
                PremiumRate = request.PremiumRate
            };
        }

        private static void ApplyRequest(
            Contract contract,
            UpsertFacultativeContractRequest request,
            string updatedBy,
            DateTime updatedAt,
            string? normalizedUmr)
        {
            contract.Umr = normalizedUmr;
            contract.Name = request.Name.Trim();
            contract.IdCounterparty = request.CounterpartyId;
            contract.IdCurrency = request.CurrencyId;
            contract.ReceptionDate = request.ReceptionDate;
            contract.InceptionDate = request.InceptionDate;
            contract.ExpiryDate = request.ExpiryDate;
            contract.Fees = request.Fees;
            contract.Flag = request.IsHighPriority;
            contract.WrittenSharePctCedent = request.WrittenSharePct;
            contract.SignedSharePctCedent = request.SignedSharePct;
            contract.GrossPremium = request.GrossPremium;
            contract.CedentCommission = request.CedentCommissionPct;
            contract.NetPremiumCedent = CalculateNetPremium(request.GrossPremium, request.CedentCommissionPct);
            contract.CedentPremiumReference = NormalizeOptionalValue(request.CedentPremiumReference);
            contract.IsFronting = request.IsFronting;
            contract.FrontingReference = request.IsFronting ? NormalizeOptionalValue(request.FrontingReference) : null;
            contract.MemoText = NormalizeOptionalValue(request.MemoText);
            contract.UpdatedAt = updatedAt;
            contract.UpdatedBy = updatedBy;
        }

        private async Task<ServiceResult<FacultativeContractResponseDto>> ValidateBusinessReferencesAsync(
            UpsertFacultativeContractRequest request,
            CancellationToken cancellationToken)
        {
            var errors = new List<string>();

            var counterparty = await _referenceDataRepository.GetActiveCedentByIdAsync(request.CounterpartyId, cancellationToken);
            if (counterparty is null)
            {
                errors.Add("The selected counterparty does not exist, is inactive, or is not eligible as a cedent.");
            }

            var currency = await _referenceDataRepository.GetActiveCurrencyByIdAsync(request.CurrencyId, cancellationToken);
            if (currency is null)
            {
                errors.Add("The selected currency does not exist or is inactive.");
            }

            return errors.Count > 0
                ? ServiceResult<FacultativeContractResponseDto>.Failure("Validation failed.", errors, StatusCodes.Status400BadRequest)
                : ServiceResult<FacultativeContractResponseDto>.Success(new FacultativeContractResponseDto());
        }

        private async Task<ServiceResult<UserProfileResponseDto>> GetCurrentUserAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;

            var principal = _httpContextAccessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true)
            {
                return ServiceResult<UserProfileResponseDto>.Failure(
                    "User identity was not found in the token.",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            return await _userService.GetCurrentUserAsync(principal);
        }

        private static ServiceResult<FacultativeContractResponseDto> MapFailure(ServiceResult<UserProfileResponseDto> userResult)
        {
            return ServiceResult<FacultativeContractResponseDto>.Failure(
                userResult.Error?.Message ?? "Unable to resolve the current user.",
                userResult.Error?.Errors,
                userResult.StatusCode);
        }

        private static bool IsFrontOfficer(UserProfileResponseDto user)
        {
            return user.Roles.Contains(IdentityRoles.Employee, StringComparer.OrdinalIgnoreCase) &&
                   string.Equals(user.Department, ContractConstants.FrontOfficerDepartment, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(user.Position, ContractConstants.FrontOfficerPosition, StringComparison.OrdinalIgnoreCase);
        }

        private static decimal? CalculateNetPremium(decimal? grossPremium, decimal? cedentCommissionPct)
        {
            if (!grossPremium.HasValue)
            {
                return null;
            }

            var commission = cedentCommissionPct ?? 0m;
            return grossPremium.Value - (grossPremium.Value * commission / 100m);
        }

        private static string? NormalizeOptionalValue(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static bool IsUmrConflict(DbUpdateException exception)
        {
            return exception.InnerException?.Message.Contains("UX_CONTRACT_UMR", StringComparison.OrdinalIgnoreCase) == true ||
                   exception.Message.Contains("UX_CONTRACT_UMR", StringComparison.OrdinalIgnoreCase);
        }

        private static ServiceResult<FacultativeContractResponseDto> Conflict(string message)
        {
            return ServiceResult<FacultativeContractResponseDto>.Failure(message, statusCode: StatusCodes.Status409Conflict);
        }

        private static ServiceResult<FacultativeContractResponseDto> Forbidden(string message)
        {
            return ServiceResult<FacultativeContractResponseDto>.Failure(message, statusCode: StatusCodes.Status403Forbidden);
        }

        private static ServiceResult<FacultativeContractResponseDto> NotFound()
        {
            return ServiceResult<FacultativeContractResponseDto>.Failure(
                "Facultative contract draft was not found.",
                statusCode: StatusCodes.Status404NotFound);
        }
    }
}
