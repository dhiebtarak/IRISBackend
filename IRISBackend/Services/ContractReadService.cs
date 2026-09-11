using IRISBackend.DTOs;
using IRISBackend.Repositories;
using System.IO.Compression;

namespace IRISBackend.Services
{
    public sealed class ContractReadService : IContractReadService
    {
        private readonly IContractRepository _contractRepository;
        private readonly IUserService _userService;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ContractReadService(
            IContractRepository contractRepository,
            IUserService userService,
            IHttpContextAccessor httpContextAccessor)
        {
            _contractRepository = contractRepository;
            _userService = userService;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<ServiceResult<PagedResponseDto<ContractListItemDto>>> ListAsync(
            ContractListQueryDto query,
            CancellationToken cancellationToken = default)
        {
            var userResult = await GetCurrentFrontOfficerAsync();
            if (!userResult.Succeeded)
            {
                return MapFailure<PagedResponseDto<ContractListItemDto>>(userResult);
            }

            var pageNumber = Math.Max(query.PageNumber, 1);
            var pageSize = Math.Clamp(query.PageSize, 1, 100);
            var normalizedQuery = NormalizeQuery(query, pageNumber, pageSize);
            var (items, totalCount) = await _contractRepository.ListAsync(
                normalizedQuery,
                userResult.Value!.Id,
                cancellationToken);

            foreach (var item in items)
            {
                item.Status = ContractConstants.NormalizeStatus(item.Status);
            }

            return ServiceResult<PagedResponseDto<ContractListItemDto>>.Success(new PagedResponseDto<ContractListItemDto>
            {
                Items = items,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)
            });
        }

        public async Task<ServiceResult<ContractDashboardSummaryDto>> GetDashboardSummaryAsync(
            CancellationToken cancellationToken = default)
        {
            var userResult = await GetCurrentFrontOfficerAsync();
            if (!userResult.Succeeded)
            {
                return MapFailure<ContractDashboardSummaryDto>(userResult);
            }

            var data = await _contractRepository.GetDashboardDataAsync(userResult.Value!.Id, cancellationToken);
            var statusCounts = data.StatusCounts
                .GroupBy(status => ContractConstants.NormalizeStatus(status.Status), StringComparer.OrdinalIgnoreCase)
                .Select(group => new ContractStatusCount
                {
                    Status = group.Key,
                    Count = group.Sum(item => item.Count)
                })
                .OrderBy(status => status.Status)
                .ToArray();

            var distribution = statusCounts
                .Select(status => new ContractWorkflowDistributionDto
                {
                    Status = status.Status,
                    Count = status.Count,
                    Percentage = data.TotalContracts == 0
                        ? 0m
                        : Math.Round(status.Count * 100m / data.TotalContracts, 2, MidpointRounding.AwayFromZero)
                })
                .ToList();

            return ServiceResult<ContractDashboardSummaryDto>.Success(new ContractDashboardSummaryDto
            {
                TotalContracts = data.TotalContracts,
                PendingReviewContracts = SumCategory(statusCounts, ContractWorkflowStatusCategories.PendingReview),
                BoundActiveContracts = SumCategory(statusCounts, ContractWorkflowStatusCategories.BoundActive),
                ActionRequiredContracts = data.ActionRequiredContracts,
                WorkflowDistribution = distribution,
                RecentContracts = data.RecentContracts
                    .Select(contract =>
                    {
                        contract.Status = ContractConstants.NormalizeStatus(contract.Status);
                        return contract;
                    })
                    .ToArray()
            });
        }

        public async Task<ServiceResult<ContractOverviewDto>> GetOverviewAsync(
            int contractId,
            CancellationToken cancellationToken = default)
        {
            var userResult = await GetCurrentFrontOfficerAsync();
            if (!userResult.Succeeded)
            {
                return MapFailure<ContractOverviewDto>(userResult);
            }

            await _contractRepository.MarkDraftReadyForPlacementAsync(contractId, userResult.Value!.Id, cancellationToken);

            var contract = await _contractRepository.GetAccessibleContractByIdAsync(
                contractId,
                userResult.Value.Id,
                cancellationToken);

            if (contract is null)
            {
                return ServiceResult<ContractOverviewDto>.Failure(
                    "Contract was not found.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            return ServiceResult<ContractOverviewDto>.Success(MapOverview(contract));
        }

        public async Task<ServiceResult<SlipDownloadResult>> DownloadSlipsAsync(
            int contractId,
            CancellationToken cancellationToken = default)
        {
            var userResult = await GetCurrentFrontOfficerAsync();
            if (!userResult.Succeeded)
            {
                return MapFailure<SlipDownloadResult>(userResult);
            }

            var contract = await _contractRepository.GetAccessibleContractByIdAsync(
                contractId,
                userResult.Value!.Id,
                cancellationToken);

            if (contract is null)
            {
                return ServiceResult<SlipDownloadResult>.Failure(
                    "Contract was not found.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var slips = await _contractRepository.ListActiveCurrentSlipTemplatesForContractAsync(
                contractId,
                userResult.Value.Id,
                cancellationToken);

            if (slips.Count == 0)
            {
                return ServiceResult<SlipDownloadResult>.Success(new SlipDownloadResult
                {
                    Available = false,
                    Message = "Slip is not available for this contract selection.",
                    SlipCount = 0
                });
            }

            if (slips.Count == 1)
            {
                var slip = slips[0];
                return ServiceResult<SlipDownloadResult>.Success(new SlipDownloadResult
                {
                    Content = slip.FileContent,
                    ContentType = string.IsNullOrWhiteSpace(slip.MimeType)
                        ? "application/octet-stream"
                        : slip.MimeType,
                    FileName = BuildSlipFileName(slip),
                    SlipCount = 1
                });
            }

            return ServiceResult<SlipDownloadResult>.Success(new SlipDownloadResult
            {
                Content = BuildSlipZip(slips),
                ContentType = "application/zip",
                FileName = $"{SanitizeFileName(contract.Name)}-slips.zip",
                SlipCount = slips.Count
            });
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
                    "Only Front Officers can access contract workspace data.",
                    statusCode: StatusCodes.Status403Forbidden);
        }

        private static ContractListQueryDto NormalizeQuery(ContractListQueryDto query, int pageNumber, int pageSize)
        {
            return new ContractListQueryDto
            {
                Search = query.Search?.Trim(),
                ContractType = ContractTypeNames.NormalizeFilter(query.ContractType),
                Status = query.Status?.Trim(),
                CounterpartyId = query.CounterpartyId,
                CurrencyId = query.CurrencyId,
                InceptionFrom = query.InceptionFrom,
                InceptionTo = query.InceptionTo,
                ExpiryFrom = query.ExpiryFrom,
                ExpiryTo = query.ExpiryTo,
                PageNumber = pageNumber,
                PageSize = pageSize,
                SortBy = query.SortBy.Trim(),
                SortDirection = query.SortDirection.Trim()
            };
        }

        private static int SumCategory(IEnumerable<ContractStatusCount> counts, IReadOnlySet<string> statuses)
        {
            return counts
                .Where(item => statuses.Contains(ContractConstants.NormalizeStatus(item.Status)))
                .Sum(item => item.Count);
        }

        private static ServiceResult<T> MapFailure<T>(ServiceResult<UserProfileResponseDto> result)
        {
            return ServiceResult<T>.Failure(
                result.Error?.Message ?? "Unable to resolve the current user.",
                result.Error?.Errors,
                result.StatusCode);
        }

        private static ContractOverviewDto MapOverview(Models.Contract contract)
        {
            var coverageSelection = MapCoverageSelection(contract);
            var selectedCount = contract.ContractCoverageSelections.Count;
            var availableSlipCount = contract.ContractCoverageSelections.Count(selection => selection.IdSlipTemplate.HasValue);

            return new ContractOverviewDto
            {
                IdContract = contract.IdContract,
                Umr = contract.Umr,
                Name = contract.Name,
                ContractType = ResolveContractType(contract),
                TypeOfBusiness = contract.TypeOfBusiness,
                Status = ContractConstants.NormalizeStatus(contract.Status),
                AllowedActions = BuildAllowedActions(contract),
                CounterpartyId = contract.IdCounterparty,
                Cedent = new CedentSummaryDto
                {
                    Id = contract.IdCounterparty,
                    Name = contract.IdCounterpartyNavigation?.Name ?? string.Empty,
                    Country = contract.IdCounterpartyNavigation?.Country,
                    City = contract.IdCounterpartyNavigation?.City
                },
                CurrencyId = contract.IdCurrency,
                Currency = new CurrencyReferenceDto
                {
                    Id = contract.IdCurrency,
                    Code = contract.IdCurrencyNavigation?.CurrencyCode ?? string.Empty,
                    Symbol = contract.IdCurrencyNavigation?.CurrencySymbol,
                    Name = contract.IdCurrencyNavigation?.Name ?? string.Empty,
                    DecimalPlaces = contract.IdCurrencyNavigation?.DecimalPlaces,
                    IsDefault = contract.IdCurrencyNavigation?.IsDefault ?? false
                },
                CreationDate = contract.CreationDate,
                ReceptionDate = contract.ReceptionDate,
                InceptionDate = contract.InceptionDate,
                ExpiryDate = contract.ExpiryDate,
                WrittenSharePct = contract.WrittenSharePctCedent,
                SignedSharePct = contract.SignedSharePctCedent,
                Tsi = contract.ContractFac?.SumInsured,
                SumInsured = contract.ContractFac?.SumInsured,
                LimitOfLiability = contract.ContractFac?.LimitOfLiability,
                Deductible = contract.ContractFac?.Deductible,
                PremiumRate = contract.ContractFac?.PremiumRate,
                Fees = contract.Fees,
                GrossPremium = contract.GrossPremium,
                CedentCommissionPct = contract.CedentCommission,
                NetPremiumCedent = contract.NetPremiumCedent,
                IsFronting = contract.IsFronting,
                FrontingReference = contract.FrontingReference,
                IsHighPriority = contract.Flag,
                ActionRequired = contract.Flag,
                CreatedBy = MapAuditUser(contract.CreatedBy, contract.CreatedByNavigation),
                CreatedAt = contract.CreatedAt,
                UpdatedBy = MapAuditUser(contract.UpdatedBy, contract.UpdatedByNavigation),
                UpdatedAt = contract.UpdatedAt,
                CoverageSelection = coverageSelection,
                SlipAvailability = new ContractSlipAvailabilityDto
                {
                    SelectedCoverageItemCount = selectedCount,
                    AvailableSlipCount = availableSlipCount,
                    MissingSlipCount = Math.Max(selectedCount - availableSlipCount, 0)
                },
                Panels = contract.ContractPanels
                    .OrderBy(panel => panel.IdPanel)
                    .Select(ContractPanelDtoMapper.Map)
                    .ToArray()
            };
        }

        private static ContractAllowedActionsDto BuildAllowedActions(Models.Contract contract)
        {
            var status = ContractConstants.NormalizeStatus(contract.Status);
            var hasConfirmedPanel = contract.ContractPanels.Any(panel =>
                string.Equals(
                    panel.Status?.Trim(),
                    ContractConstants.ConfirmedPanelStatus,
                    StringComparison.OrdinalIgnoreCase));

            var canModify = status is ContractConstants.DraftStatus or
                ContractConstants.ReadyForPlacementStatus or
                ContractConstants.PlacementInProgressStatus;
            var canPlace = status is ContractConstants.ReadyForPlacementStatus or
                ContractConstants.PlacementInProgressStatus;

            return new ContractAllowedActionsDto
            {
                CanModifyContract = canModify,
                CanAddReinsurer = canPlace,
                CanStartBinding = status == ContractConstants.PlacementInProgressStatus && hasConfirmedPanel,
                CanCancelContract = canModify,
                CanEditPlacement = status == ContractConstants.PlacementInProgressStatus
            };
        }

        private static ContractAuditUserDto? MapAuditUser(string? userId, Models.AspNetUser? user)
        {
            if (string.IsNullOrWhiteSpace(userId) && user is null)
            {
                return null;
            }

            return new ContractAuditUserDto
            {
                Id = user?.Id ?? userId,
                FullName = user?.FullName,
                UserName = user?.UserName,
                Email = user?.Email
            };
        }

        private static ContractCoverageSelectionResponseDto? MapCoverageSelection(Models.Contract contract)
        {
            if (contract.ContractCoverageSelections.Count == 0)
            {
                return null;
            }

            var selections = contract.ContractCoverageSelections
                .Where(selection => selection.IdCoverageItemNavigation is not null)
                .ToList();

            var mainCovers = selections
                .Where(selection => IsCoverageLevel(selection, CoverageConstants.MainCoverLevel))
                .OrderByDescending(selection => selection.IsPrimary)
                .ThenBy(selection => selection.IdCoverageItem)
                .ToArray();
            var primaryMainCover = mainCovers.FirstOrDefault();

            var sections = selections
                .Where(selection => IsCoverageLevel(selection, CoverageConstants.SectionLevel))
                .OrderBy(selection => selection.IdCoverageItemNavigation.DisplayOrder ?? int.MaxValue)
                .ThenBy(selection => selection.IdCoverageItemNavigation.CoverageName)
                .Select(MapCoverageItem)
                .ToArray();

            var extensions = selections
                .Where(selection => IsCoverageLevel(selection, CoverageConstants.ExtensionLevel))
                .OrderBy(selection => selection.IdCoverageItemNavigation.DisplayOrder ?? int.MaxValue)
                .ThenBy(selection => selection.IdCoverageItemNavigation.CoverageName)
                .Select(MapCoverageItem)
                .ToArray();

            return new ContractCoverageSelectionResponseDto
            {
                PrimaryMainCoverId = primaryMainCover?.IdCoverageItem ?? 0,
                CoverageIds = mainCovers.Select(selection => selection.IdCoverageItem).ToArray(),
                Coverages = mainCovers.Select((selection, index) => MapMainCoverItem(selection, index == 0)).ToArray(),
                MainCoverId = primaryMainCover?.IdCoverageItem ?? 0,
                SectionIds = sections.Select(section => section.Id).ToArray(),
                ExtensionIds = extensions.Select(extension => extension.Id).ToArray(),
                MainCover = primaryMainCover is null ? null : MapCoverageItem(primaryMainCover),
                Sections = sections,
                Extensions = extensions
            };
        }

        private static ContractCoverageSelectionItemDto MapCoverageItem(Models.ContractCoverageSelection selection)
        {
            return new ContractCoverageSelectionItemDto
            {
                Id = selection.IdCoverageItem,
                Code = selection.IdCoverageItemNavigation.CoverageCode,
                Name = selection.IdCoverageItemNavigation.CoverageName,
                HasSlip = selection.IdSlipTemplate.HasValue,
                SlipTemplateId = selection.IdSlipTemplate
            };
        }

        private static ContractMainCoverSelectionItemDto MapMainCoverItem(Models.ContractCoverageSelection selection, bool primaryFallback)
        {
            return new ContractMainCoverSelectionItemDto
            {
                IdMainCover = selection.IdCoverageItem,
                Code = selection.IdCoverageItemNavigation.CoverageCode,
                Description = selection.IdCoverageItemNavigation.Description ?? selection.IdCoverageItemNavigation.CoverageName,
                IsPrimary = selection.IsPrimary || primaryFallback,
                HasSlip = selection.IdSlipTemplate.HasValue,
                SlipTemplateId = selection.IdSlipTemplate
            };
        }

        private static bool IsCoverageLevel(Models.ContractCoverageSelection selection, string level)
        {
            return string.Equals(
                selection.IdCoverageItemNavigation.CoverageLevel?.Trim(),
                level,
                StringComparison.OrdinalIgnoreCase);
        }

        private static string? ResolveContractType(Models.Contract contract)
        {
            if (contract.ContractFac is not null)
            {
                return ContractTypeNames.Facultative;
            }

            if (contract.ContractTreatyProp?.ContractTreatyPropQuotaShare is not null)
            {
                return ContractTypeNames.TreatyProportionalQuotaShare;
            }

            if (contract.ContractTreatyProp?.ContractTreatyPropSurplu is not null)
            {
                return ContractTypeNames.TreatyProportionalSurplus;
            }

            return contract.ContractTreatyNonprops.Count > 0 ? ContractTypeNames.TreatyNonProportional : null;
        }

        private static byte[] BuildSlipZip(IReadOnlyList<Models.SlipTemplate> slips)
        {
            using var memoryStream = new MemoryStream();
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var slip in slips)
                {
                    var entry = archive.CreateEntry(BuildSlipFileName(slip), CompressionLevel.Fastest);
                    using var entryStream = entry.Open();
                    entryStream.Write(slip.FileContent, 0, slip.FileContent.Length);
                }
            }

            return memoryStream.ToArray();
        }

        private static string BuildSlipFileName(Models.SlipTemplate slip)
        {
            var originalName = SanitizeFileName(slip.OriginalFileName);
            if (!string.IsNullOrWhiteSpace(originalName))
            {
                return originalName;
            }

            var extension = string.IsNullOrWhiteSpace(slip.FileExtension)
                ? ".docx"
                : slip.FileExtension.Trim();

            if (!extension.StartsWith(".", StringComparison.Ordinal))
            {
                extension = "." + extension;
            }

            return $"slip-template-{slip.IdSlipTemplate}{extension}";
        }

        private static string SanitizeFileName(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return "contract";
            }

            var invalidCharacters = Path.GetInvalidFileNameChars();
            var sanitized = new string(fileName
                .Select(character => invalidCharacters.Contains(character) ? '_' : character)
                .ToArray())
                .Trim();

            return string.IsNullOrWhiteSpace(sanitized) ? "contract" : sanitized;
        }
    }
}
