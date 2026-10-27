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
        private readonly PremiumCalculationService _premiumCalculationService;

        public FacultativeContractService(
            IContractRepository contractRepository,
            IReferenceDataRepository referenceDataRepository,
            ICoverageSelectionService coverageSelectionService,
            IContractUmrGenerator umrGenerator,
            IUserService userService,
            IMapper mapper,
            IHttpContextAccessor httpContextAccessor,
            PremiumCalculationService? premiumCalculationService = null)
        {
            _contractRepository = contractRepository;
            _referenceDataRepository = referenceDataRepository;
            _coverageSelectionService = coverageSelectionService;
            _umrGenerator = umrGenerator;
            _userService = userService;
            _mapper = mapper;
            _httpContextAccessor = httpContextAccessor;
            _premiumCalculationService = premiumCalculationService ?? new PremiumCalculationService();
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
                return MapBusinessReferenceFailure(validationResult);
            }
            var currencyPrecision = validationResult.Value?.DecimalPlaces;

            CoverageSelectionPlan? coveragePlan = null;
            var coverageRequest = NormalizeCompleteCoverageSelection(request.CoverageSelection);
            if (coverageRequest is not null)
            {
                var coverageValidationResult = await _coverageSelectionService.ValidateAsync(coverageRequest, cancellationToken);
                if (!coverageValidationResult.Succeeded) return MapCoverageFailure(coverageValidationResult);
                coveragePlan = coverageValidationResult.Value;
            }

            var now = DateTime.UtcNow;
            var contractFac = HasFacultativeDetails(request) ? BuildContractFacEntity(request) : null;

            await using IDbContextTransaction transaction = await _contractRepository.BeginTransactionAsync(cancellationToken);
            Contract? contract = null;

            try
            {
                contract = BuildContractEntity(request, currentUserResult.Value!, now, currencyPrecision);
                await _contractRepository.AddAsync(contract, cancellationToken);
                await _contractRepository.SaveChangesAsync(cancellationToken);

                if (contractFac is not null)
                {
                    contractFac.IdContract = contract.IdContract;
                    await _contractRepository.AddFacultativeDetailsAsync(contractFac, cancellationToken);
                    await _contractRepository.SaveChangesAsync(cancellationToken);
                }

                if (coveragePlan is not null)
                    await _coverageSelectionService.CreateSelectionsAsync(contract.IdContract, coveragePlan,
                        currentUserResult.Value!.Id, now, cancellationToken);

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

        public async Task<ServiceResult<FacultativeContractResponseDto>> CompleteDraftAsync(
            int contractId,
            CancellationToken cancellationToken = default)
        {
            var currentUserResult = await GetCurrentUserAsync(cancellationToken);
            if (!currentUserResult.Succeeded) return MapFailure(currentUserResult);
            var currentUser = currentUserResult.Value!;
            if (!FrontOfficerAccessRules.IsFrontOfficer(currentUser))
                return Forbidden("Only Front Officers can complete a contract draft.");

            await using var transaction = await _contractRepository.BeginTransactionAsync(cancellationToken);
            try
            {
                var contract = await _contractRepository.GetFacultativeContractByIdAsync(contractId,
                    cancellationToken: cancellationToken);
                if (contract is null) return NotFound();
                if (!string.Equals(contract.IdUser, currentUser.Id, StringComparison.Ordinal))
                    return Forbidden("You do not have access to complete another user's draft.");
                if (!string.Equals(ContractConstants.NormalizeStatus(contract.Status), ContractConstants.DraftStatus,
                        StringComparison.OrdinalIgnoreCase))
                    return Conflict($"Only a Draft contract can move to Ready for Placement. Current status: '{contract.Status}'.");

                var errors = ValidateForReadyForPlacement(contract);
                if (errors.Count > 0)
                    return ServiceResult<FacultativeContractResponseDto>.Failure(
                        "Contract validation failed.", errors, StatusCodes.Status400BadRequest);

                var referenceValidation = await ValidateBusinessReferencesAsync(new UpsertFacultativeContractRequest
                {
                    CounterpartyId = contract.IdCounterparty,
                    CurrencyId = contract.IdCurrency
                }, cancellationToken);
                if (!referenceValidation.Succeeded)
                    return MapBusinessReferenceFailure(referenceValidation);

                var coverageRequest = BuildCoverageSelectionRequest(contract);
                var coverageValidation = await _coverageSelectionService.ValidateAsync(coverageRequest, cancellationToken);
                if (!coverageValidation.Succeeded) return MapCoverageFailure(coverageValidation);

                var completedRequest = MergeRequest(contract, new UpsertFacultativeContractRequest());
                var financialValidation = ValidateFinancialInputs(completedRequest);
                if (!financialValidation.Succeeded) return financialValidation;
                var currencyPrecision = referenceValidation.Value?.DecimalPlaces;
                var financials = CalculateFinancials(completedRequest, currencyPrecision);
                if (financials is null)
                    return ServiceResult<FacultativeContractResponseDto>.Failure(
                        "Contract validation failed.", ["premiumRate: Sum insured and premium rate are required before placement."], StatusCodes.Status400BadRequest);

                if (contract.Umr is null)
                {
                    var umrResult = await _umrGenerator.GenerateAsync(
                        ContractConstants.FacultativeType,
                        contract.InceptionDate!.Value,
                        coverageValidation.Value!,
                        cancellationToken);
                    if (!umrResult.Succeeded)
                        return ServiceResult<FacultativeContractResponseDto>.Failure(
                            umrResult.Error?.Message ?? "Unable to generate the contract UMR.",
                            umrResult.Error?.Errors,
                            umrResult.StatusCode);
                    contract.Umr = umrResult.Value;
                }

                contract.GrossPremium = financials.GrossPremium;
                contract.CedentCommission = contract.CedentCommission ?? 0m;
                contract.NetPremiumCedent = PremiumCalculationService.CalculateContractNetPremium(
                    financials.GrossPremium, contract.CedentCommission.Value, currencyPrecision);
                contract.Status = ContractConstants.ReadyForPlacementStatus;
                contract.UpdatedAt = DateTime.UtcNow;
                contract.UpdatedBy = currentUser.Id;
                _contractRepository.Update(contract);
                await _contractRepository.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                var saved = await _contractRepository.GetFacultativeContractByIdAsync(contractId,
                    asNoTracking: true, cancellationToken);
                return ServiceResult<FacultativeContractResponseDto>.Success(
                    _mapper.Map<FacultativeContractResponseDto>(saved!));
            }
            catch (DbUpdateException exception) when (IsUmrConflict(exception))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return Conflict("A contract with the generated UMR already exists.");
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
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
                return MapBusinessReferenceFailure(validationResult);
            }
            var currencyPrecision = validationResult.Value?.DecimalPlaces ?? contract.IdCurrencyNavigation?.DecimalPlaces;

            CoverageSelectionPlan? coveragePlan = null;
            var coverageRequest = NormalizeCompleteCoverageSelection(request.CoverageSelection);
            if (coverageRequest is not null)
            {
                var coverageValidationResult = await _coverageSelectionService.ValidateAsync(coverageRequest, cancellationToken);
                if (!coverageValidationResult.Succeeded) return MapCoverageFailure(coverageValidationResult);
                coveragePlan = coverageValidationResult.Value;
            }

            var effectiveRequest = MergeRequest(contract, request);
            ApplyRequest(contract, effectiveRequest, currentUserResult.Value!.Id, DateTime.UtcNow, currencyPrecision);

            if (contract.ContractFac is null && HasFacultativeDetails(request))
            {
                contract.ContractFac = new ContractFac
                {
                    IdContract = contract.IdContract
                };
            }

            if (contract.ContractFac is not null)
            {
                if (request.SumInsured.HasValue) contract.ContractFac.SumInsured = request.SumInsured;
                if (request.LimitOfLiability.HasValue) contract.ContractFac.LimitOfLiability = request.LimitOfLiability;
                if (request.Deductible.HasValue) contract.ContractFac.Deductible = request.Deductible;
                if (request.PremiumRate.HasValue) contract.ContractFac.PremiumRate = request.PremiumRate;
            }

            await using IDbContextTransaction transaction = await _contractRepository.BeginTransactionAsync(cancellationToken);
            try
            {
                _contractRepository.Update(contract);
                await _contractRepository.SaveChangesAsync(cancellationToken);

                if (coveragePlan is not null)
                    await _coverageSelectionService.ReplaceSelectionsAsync(contract.IdContract, coveragePlan,
                        currentUserResult.Value!.Id, contract.UpdatedAt ?? DateTime.UtcNow, cancellationToken);

                if (contract.ContractPanels.Count > 0 && HasCompletePremiumInputs(contract))
                {
                    var premiumErrors = _premiumCalculationService.RecalculatePanels(contract, currencyPrecision);
                    if (premiumErrors.Count > 0)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return ServiceResult<FacultativeContractResponseDto>.Failure(
                            "Premium validation failed.", premiumErrors, StatusCodes.Status400BadRequest);
                    }
                }

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

            var refreshedContract = await _contractRepository.GetFacultativeContractByIdAsync(contractId, asNoTracking: true, cancellationToken);
            return ServiceResult<FacultativeContractResponseDto>.Success(_mapper.Map<FacultativeContractResponseDto>(refreshedContract!));
        }

        private Contract BuildContractEntity(
            UpsertFacultativeContractRequest request,
            UserProfileResponseDto currentUser,
            DateTime now,
            int? currencyPrecision)
        {
            var financials = CalculateFinancials(request, currencyPrecision);
            var contract = new Contract
            {
                Umr = null,
                Name = NormalizeOptionalValue(request.Name),
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
                NetPremiumCedent = financials is null ? null : PremiumCalculationService.CalculateContractNetPremium(
                    financials.GrossPremium, request.CedentCommissionPct ?? 0m, currencyPrecision),
                CedentPremiumReference = NormalizeOptionalValue(request.CedentPremiumReference),
                IsFronting = request.IsFronting,
                FrontingReference = request.IsFronting == true ? NormalizeOptionalValue(request.FrontingReference) : null,
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
            DateTime updatedAt,
            int? currencyPrecision)
        {
            contract.Name = NormalizeOptionalValue(request.Name);
            contract.IdCounterparty = request.CounterpartyId;
            contract.IdCurrency = request.CurrencyId;
            contract.ReceptionDate = request.ReceptionDate;
            contract.InceptionDate = request.InceptionDate;
            contract.ExpiryDate = request.ExpiryDate;
            contract.Fees = request.Fees;
            contract.Flag = request.IsHighPriority;
            contract.WrittenSharePctCedent = request.WrittenSharePct;
            contract.SignedSharePctCedent = request.SignedSharePct;
            var financials = CalculateFinancials(request, currencyPrecision);
            contract.GrossPremium = financials?.GrossPremium;
            contract.CedentCommission = request.CedentCommissionPct;
            contract.NetPremiumCedent = financials is null ? null : PremiumCalculationService.CalculateContractNetPremium(
                financials.GrossPremium, request.CedentCommissionPct ?? 0m, currencyPrecision);
            contract.CedentPremiumReference = NormalizeOptionalValue(request.CedentPremiumReference);
            contract.IsFronting = request.IsFronting;
            contract.FrontingReference = request.IsFronting == true ? NormalizeOptionalValue(request.FrontingReference) : null;
            contract.MemoText = NormalizeOptionalValue(request.MemoText);
            contract.UpdatedAt = updatedAt;
            contract.UpdatedBy = updatedBy;
        }

        private async Task<ServiceResult<Currency?>> ValidateBusinessReferencesAsync(
            UpsertFacultativeContractRequest request,
            CancellationToken cancellationToken)
        {
            var errors = new List<string>();

            if (request.CounterpartyId.HasValue &&
                await _referenceDataRepository.GetActiveCedentByIdAsync(request.CounterpartyId.Value, cancellationToken) is null)
            {
                errors.Add("counterpartyId: The selected counterparty does not exist, is inactive, or is not eligible as a cedent.");
            }

            var currency = request.CurrencyId.HasValue
                ? await _referenceDataRepository.GetActiveCurrencyByIdAsync(request.CurrencyId.Value, cancellationToken)
                : null;
            if (request.CurrencyId.HasValue && currency is null)
            {
                errors.Add("currencyId: The selected currency does not exist or is inactive.");
            }

            return errors.Count > 0
                ? ServiceResult<Currency?>.Failure("Validation failed.", errors, StatusCodes.Status400BadRequest)
                : ServiceResult<Currency?>.Success(currency);
        }

        private static ServiceResult<FacultativeContractResponseDto> MapBusinessReferenceFailure(ServiceResult<Currency?> result) =>
            ServiceResult<FacultativeContractResponseDto>.Failure(
                result.Error?.Message ?? "Business reference validation failed.",
                result.Error?.Errors,
                result.StatusCode);

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

        private static FacultativeFinancialCalculation? CalculateFinancials(
            UpsertFacultativeContractRequest request,
            int? currencyDecimalPlaces = null)
        {
            return FacultativeFinancialCalculator.Calculate(
                request.SumInsured,
                request.PremiumRate,
                request.Fees,
                request.CedentCommissionPct,
                currencyDecimalPlaces);
        }

        private static ServiceResult<FacultativeContractResponseDto> ValidateFinancialInputs(UpsertFacultativeContractRequest request)
        {
            var errors = new List<string>();
            if (request.SumInsured is < 0m)
            {
                errors.Add("sumInsured (TSI) must be greater than or equal to 0.");
            }

            if (request.PremiumRate is < 0m or > 100m)
            {
                errors.Add("premiumRate must be between 0 and 100 because it is a percentage.");
            }

            if (request.Fees is < 0m)
            {
                errors.Add("fees (IRIS commission) must be greater than or equal to 0.");
            }

            if (request.CedentCommissionPct is < 0m or > 100m)
            {
                errors.Add("cedentCommissionPct must be between 0 and 100 because it is a percentage.");
            }

            var calculation = CalculateFinancials(request);
            if (calculation is not null)
            {
                var cedentCommissionAmount = calculation.GrossPremium * (request.CedentCommissionPct ?? 0m) / 100m;
                if (cedentCommissionAmount > calculation.GrossPremium)
                {
                    errors.Add("Cedent commission amount cannot exceed gross premium.");
                }

                if (calculation.GrossPremium - cedentCommissionAmount < 0m)
                {
                    errors.Add("Calculated contract net premium cannot be negative.");
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

        private static bool HasFacultativeDetails(UpsertFacultativeContractRequest request) =>
            request.SumInsured.HasValue || request.LimitOfLiability.HasValue
            || request.Deductible.HasValue || request.PremiumRate.HasValue;

        private static CoverageSelectionRequestDto? NormalizeCompleteCoverageSelection(CoverageSelectionRequestDto? request)
        {
            if (request is null) return null;
            var mainIds = request.CoverageIds?.Where(id => id > 0).Distinct().ToArray() ?? [];
            var primaryId = request.PrimaryMainCoverId > 0
                ? request.PrimaryMainCoverId
                : request.MainCoverId > 0 ? request.MainCoverId : mainIds.FirstOrDefault();
            if (primaryId <= 0) return null;
            if (mainIds.Length == 0) mainIds = [primaryId];
            if (!mainIds.Contains(primaryId)) mainIds = [primaryId, .. mainIds];
            return new CoverageSelectionRequestDto
            {
                PrimaryMainCoverId = primaryId,
                MainCoverId = primaryId,
                CoverageIds = mainIds,
                SectionIds = request.SectionIds?.Where(id => id > 0).Distinct().ToArray() ?? [],
                ExtensionIds = request.ExtensionIds?.Where(id => id > 0).Distinct().ToArray() ?? []
            };
        }

        private static bool HasCompletePremiumInputs(Contract contract) =>
            contract.ContractFac?.SumInsured.HasValue == true
            && contract.ContractFac.PremiumRate.HasValue;

        private static List<string> ValidateForReadyForPlacement(Contract contract)
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(contract.Name)) errors.Add("name: Contract name is required before placement.");
            if (!contract.IdCounterparty.HasValue) errors.Add("counterpartyId: Cedent is required before placement.");
            if (!contract.IdCurrency.HasValue) errors.Add("currencyId: Currency is required before placement.");
            if (!contract.InceptionDate.HasValue) errors.Add("inceptionDate: Inception date is required before placement.");
            if (!contract.ExpiryDate.HasValue) errors.Add("expiryDate: Expiry date is required before placement.");
            if (contract.InceptionDate.HasValue && contract.ExpiryDate.HasValue && contract.ExpiryDate < contract.InceptionDate)
                errors.Add("expiryDate: Expiry date must be on or after inception date.");
            if (contract.ContractFac?.SumInsured is null) errors.Add("sumInsured: Sum insured is required before placement.");
            if (contract.ContractFac?.PremiumRate is null) errors.Add("premiumRate: Premium rate is required before placement.");
            if (!contract.CedentCommission.HasValue) errors.Add("cedentCommissionPct: Cedent commission is required before placement.");
            if (!contract.WrittenSharePctCedent.HasValue) errors.Add("writtenSharePct: Written share is required before placement.");
            if (!contract.SignedSharePctCedent.HasValue) errors.Add("signedSharePct: Signed share is required before placement.");
            if (contract.IsFronting == true && string.IsNullOrWhiteSpace(contract.FrontingReference))
                errors.Add("frontingReference: Fronting reference is required for a fronting contract.");

            if (contract.ContractCoverageSelections.Count == 0)
                errors.Add("coverageSelection: At least one main coverage is required before placement.");

            if (contract.ContractFac?.SumInsured is < 0m) errors.Add("sumInsured: Sum insured cannot be negative.");
            if (contract.ContractFac?.PremiumRate is < 0m or > 100m) errors.Add("premiumRate: Premium rate must be between 0 and 100.");
            if (contract.CedentCommission is < 0m or > 100m) errors.Add("cedentCommissionPct: Cedent commission must be between 0 and 100.");
            if (contract.WrittenSharePctCedent is < 0m or > 100m) errors.Add("writtenSharePct: Written share must be between 0 and 100.");
            if (contract.SignedSharePctCedent is < 0m or > 100m or 0m) errors.Add("signedSharePct: Signed share must be greater than zero and no greater than 100.");
            if (contract.WrittenSharePctCedent.HasValue && contract.SignedSharePctCedent > contract.WrittenSharePctCedent)
                errors.Add("signedSharePct: Signed share cannot exceed written share.");

            return errors;
        }

        private static CoverageSelectionRequestDto BuildCoverageSelectionRequest(Contract contract)
        {
            var selections = contract.ContractCoverageSelections
                .Where(selection => selection.IdCoverageItemNavigation is not null)
                .ToList();
            var mains = selections.Where(selection => CoverageConstants.IsMainCoverLevel(selection.IdCoverageItemNavigation.CoverageLevel)).ToList();
            var primary = mains.FirstOrDefault(selection => selection.IsPrimary) ?? mains.FirstOrDefault();
            return new CoverageSelectionRequestDto
            {
                PrimaryMainCoverId = primary?.IdCoverageItem ?? 0,
                MainCoverId = primary?.IdCoverageItem ?? 0,
                CoverageIds = mains.Select(selection => selection.IdCoverageItem).Distinct().ToArray(),
                SectionIds = selections.Where(selection => IsCoverageLevel(selection.IdCoverageItemNavigation.CoverageLevel, CoverageConstants.SectionLevel))
                    .Select(selection => selection.IdCoverageItem).Distinct().ToArray(),
                ExtensionIds = selections.Where(selection => IsCoverageLevel(selection.IdCoverageItemNavigation.CoverageLevel, CoverageConstants.ExtensionLevel))
                    .Select(selection => selection.IdCoverageItem).Distinct().ToArray()
            };
        }

        private static bool IsCoverageLevel(string? actual, string expected) =>
            string.Equals(actual?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

        private static UpsertFacultativeContractRequest MergeRequest(
            Contract contract,
            UpsertFacultativeContractRequest request)
        {
            return new UpsertFacultativeContractRequest
            {
                Name = request.Name ?? contract.Name,
                CounterpartyId = request.CounterpartyId ?? contract.IdCounterparty,
                CurrencyId = request.CurrencyId ?? contract.IdCurrency,
                ReceptionDate = request.ReceptionDate ?? contract.ReceptionDate,
                InceptionDate = request.InceptionDate ?? contract.InceptionDate,
                ExpiryDate = request.ExpiryDate ?? contract.ExpiryDate,
                WrittenSharePct = request.WrittenSharePct ?? contract.WrittenSharePctCedent,
                SignedSharePct = request.SignedSharePct ?? contract.SignedSharePctCedent,
                Fees = request.Fees ?? contract.Fees,
                IsHighPriority = request.IsHighPriority ?? contract.Flag,
                SumInsured = request.SumInsured ?? contract.ContractFac?.SumInsured,
                LimitOfLiability = request.LimitOfLiability ?? contract.ContractFac?.LimitOfLiability,
                Deductible = request.Deductible ?? contract.ContractFac?.Deductible,
                PremiumRate = request.PremiumRate ?? contract.ContractFac?.PremiumRate,
                CedentCommissionPct = request.CedentCommissionPct ?? contract.CedentCommission,
                CedentPremiumReference = request.CedentPremiumReference ?? contract.CedentPremiumReference,
                IsFronting = request.IsFronting ?? contract.IsFronting,
                FrontingReference = request.FrontingReference ?? contract.FrontingReference,
                MemoText = request.MemoText ?? contract.MemoText
            };
        }

        private static bool CanUpdateContract(string? status)
        {
            var effectiveStatus = ContractConstants.NormalizeStatus(status);
            return effectiveStatus == ContractConstants.DraftStatus;
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
