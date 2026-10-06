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
        private readonly ICoverageSelectionService _coverageSelectionService;
        private readonly IContractUmrGenerator _umrGenerator;
        private readonly IUserService _userService;
        private readonly IMapper _mapper;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public FacultativeContractService(
            IContractRepository contractRepository,
            IReferenceDataRepository referenceDataRepository,
            ICoverageSelectionService coverageSelectionService,
            IContractUmrGenerator umrGenerator,
            IUserService userService,
            IMapper mapper,
            IHttpContextAccessor httpContextAccessor)
        {
            _contractRepository = contractRepository;
            _referenceDataRepository = referenceDataRepository;
            _coverageSelectionService = coverageSelectionService;
            _umrGenerator = umrGenerator;
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

            if (!FrontOfficerAccessRules.IsFrontOfficer(currentUserResult.Value!))
            {
                return Forbidden("Only Front Officers can create facultative contract drafts.");
            }

            var financialValidation = ValidateFinancialInputs(request);
            if (!financialValidation.Succeeded)
            {
                return financialValidation;
            }

            var validationResult = await ValidateBusinessReferencesAsync(request, cancellationToken);
            if (!validationResult.Succeeded)
            {
                return validationResult;
            }

            var coverageValidationResult = await _coverageSelectionService.ValidateAsync(request.CoverageSelection, cancellationToken);
            if (!coverageValidationResult.Succeeded)
            {
                return MapCoverageFailure(coverageValidationResult);
            }

            var now = DateTime.UtcNow;
            var contractFac = BuildContractFacEntity(request);

            await using IDbContextTransaction transaction = await _contractRepository.BeginTransactionAsync(cancellationToken);
            Contract? contract = null;

            try
            {
                var umrResult = await _umrGenerator.GenerateAsync(
                    ContractConstants.FacultativeType,
                    request.InceptionDate,
                    coverageValidationResult.Value!,
                    cancellationToken);
                if (!umrResult.Succeeded)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return ServiceResult<FacultativeContractResponseDto>.Failure(
                        umrResult.Error?.Message ?? "Unable to generate the contract UMR.",
                        umrResult.Error?.Errors,
                        umrResult.StatusCode);
                }

                contract = BuildContractEntity(request, currentUserResult.Value!, umrResult.Value!, now);
                await _contractRepository.AddAsync(contract, cancellationToken);
                await _contractRepository.SaveChangesAsync(cancellationToken);

                contractFac.IdContract = contract.IdContract;
                await _contractRepository.AddFacultativeDetailsAsync(contractFac, cancellationToken);
                await _contractRepository.SaveChangesAsync(cancellationToken);

                await _coverageSelectionService.CreateSelectionsAsync(
                    contract.IdContract,
                    coverageValidationResult.Value!,
                    currentUserResult.Value!.Id,
                    now,
                    cancellationToken);

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

            var savedContract = await _contractRepository.GetFacultativeContractByIdAsync(contract!.IdContract, asNoTracking: true, cancellationToken);
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

            if (!FrontOfficerAccessRules.IsFrontOfficer(currentUserResult.Value!))
            {
                return Forbidden("Only Front Officers can update facultative contract drafts.");
            }

            var financialValidation = ValidateFinancialInputs(request);
            if (!financialValidation.Succeeded)
            {
                return financialValidation;
            }

            var contract = await _contractRepository.GetFacultativeContractByIdAsync(contractId, cancellationToken: cancellationToken);
            if (contract is null)
            {
                return NotFound();
            }

            if (!CanUpdateContract(contract.Status))
            {
                return Conflict($"Facultative contract cannot be updated while its status is '{contract.Status}'.");
            }

            if (!string.Equals(contract.IdUser, currentUserResult.Value!.Id, StringComparison.Ordinal))
            {
                return Forbidden("You do not have access to update another user's draft.");
            }

            var validationResult = await ValidateBusinessReferencesAsync(request, cancellationToken);
            if (!validationResult.Succeeded)
            {
                return validationResult;
            }

            var coverageValidationResult = await _coverageSelectionService.ValidateAsync(request.CoverageSelection, cancellationToken);
            if (!coverageValidationResult.Succeeded)
            {
                return MapCoverageFailure(coverageValidationResult);
            }

            ApplyRequest(contract, request, currentUserResult.Value!.Id, DateTime.UtcNow);

            if (contract.ContractFac is null)
            {
                contract.ContractFac = new ContractFac
                {
                    IdContract = contract.IdContract
                };
            }

            contract.ContractFac.SumInsured = request.SumInsured;
            contract.ContractFac.LimitOfLiability = request.LimitOfLiability;
            contract.ContractFac.Deductible = request.Deductible;
            contract.ContractFac.PremiumRate = request.PremiumRate;

            await using IDbContextTransaction transaction = await _contractRepository.BeginTransactionAsync(cancellationToken);
            try
            {
                _contractRepository.Update(contract);
                await _contractRepository.SaveChangesAsync(cancellationToken);

                await _coverageSelectionService.ReplaceSelectionsAsync(
                    contract.IdContract,
                    coverageValidationResult.Value!,
                    currentUserResult.Value!.Id,
                    contract.UpdatedAt ?? DateTime.UtcNow,
                    cancellationToken);

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

            var refreshedContract = await _contractRepository.GetFacultativeContractByIdAsync(contractId, asNoTracking: true, cancellationToken);
            return ServiceResult<FacultativeContractResponseDto>.Success(_mapper.Map<FacultativeContractResponseDto>(refreshedContract!));
        }

        private Contract BuildContractEntity(
            UpsertFacultativeContractRequest request,
            UserProfileResponseDto currentUser,
            string umr,
            DateTime now)
        {
            var financials = CalculateFinancials(request);
            var contract = new Contract
            {
                Umr = umr,
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
                GrossPremium = financials?.GrossPremium,
                CedentCommission = request.CedentCommissionPct,
                NetPremiumCedent = financials?.NetPremium,
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
                LimitOfLiability = request.LimitOfLiability,
                Deductible = request.Deductible,
                PremiumRate = request.PremiumRate
            };
        }

        private static void ApplyRequest(
            Contract contract,
            UpsertFacultativeContractRequest request,
            string updatedBy,
            DateTime updatedAt)
        {
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
            var financials = CalculateFinancials(request);
            contract.GrossPremium = financials?.GrossPremium;
            contract.CedentCommission = request.CedentCommissionPct;
            contract.NetPremiumCedent = financials?.NetPremium;
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

        private static ServiceResult<FacultativeContractResponseDto> MapCoverageFailure(ServiceResult<CoverageSelectionPlan> coverageResult)
        {
            return ServiceResult<FacultativeContractResponseDto>.Failure(
                coverageResult.Error?.Message ?? "Coverage selection is invalid.",
                coverageResult.Error?.Errors,
                coverageResult.StatusCode);
        }

        private static FacultativeFinancialCalculation? CalculateFinancials(UpsertFacultativeContractRequest request)
        {
            return FacultativeFinancialCalculator.Calculate(
                request.SumInsured,
                request.PremiumRate,
                request.Fees,
                request.CedentCommissionPct);
        }

        private static ServiceResult<FacultativeContractResponseDto> ValidateFinancialInputs(UpsertFacultativeContractRequest request)
        {
            var errors = new List<string>();
            if (request.SumInsured is < 0m)
            {
                errors.Add("sumInsured (TSI) must be greater than or equal to 0.");
            }

            if (request.PremiumRate is < 0m or > 1m)
            {
                errors.Add("premiumRate must be between 0 and 1 because it is stored as a fraction.");
            }

            if (request.Fees is < 0m)
            {
                errors.Add("fees (IRIS commission) must be greater than or equal to 0.");
            }

            var calculation = CalculateFinancials(request);
            if (calculation is not null)
            {
                if (calculation.IrisCommission + calculation.CedentCommission > calculation.GrossPremium)
                {
                    errors.Add("IRIS commission and Cedent commission cannot exceed gross premium.");
                }

                if (calculation.NetPremium < 0m)
                {
                    errors.Add("Calculated net premium cannot be negative.");
                }
            }

            return errors.Count == 0
                ? ServiceResult<FacultativeContractResponseDto>.Success(new FacultativeContractResponseDto())
                : ServiceResult<FacultativeContractResponseDto>.Failure("Validation failed.", errors, StatusCodes.Status400BadRequest);
        }

        private static string? NormalizeOptionalValue(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static bool CanUpdateContract(string? status)
        {
            var effectiveStatus = ContractConstants.NormalizeStatus(status);
            return effectiveStatus is ContractConstants.DraftStatus or
                ContractConstants.ReadyForPlacementStatus or
                ContractConstants.PlacementInProgressStatus;
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
