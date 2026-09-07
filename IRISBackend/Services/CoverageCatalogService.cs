using IRISBackend.DTOs;
using IRISBackend.Repositories;

namespace IRISBackend.Services
{
    public sealed class CoverageCatalogService : ICoverageCatalogService
    {
        private readonly ICoverageRepository _coverageRepository;

        public CoverageCatalogService(ICoverageRepository coverageRepository)
        {
            _coverageRepository = coverageRepository;
        }

        public async Task<ServiceResult<IEnumerable<CoverageCatalogItemDto>>> GetMainCoversAsync(CancellationToken cancellationToken = default)
        {
            var items = await _coverageRepository.ListMainCoversAsync(cancellationToken);
            return ServiceResult<IEnumerable<CoverageCatalogItemDto>>.Success(items);
        }

        public async Task<ServiceResult<IEnumerable<CoverageCatalogItemDto>>> GetChildrenAsync(
            int parentCoverageItemId,
            string? level,
            CancellationToken cancellationToken = default)
        {
            if (parentCoverageItemId <= 0)
            {
                return Failure("parentCoverageItemId must be greater than 0.");
            }

            var normalizedLevel = Normalize(level);
            var relationType = normalizedLevel switch
            {
                CoverageConstants.SectionLevel => CoverageConstants.HasSectionRelation,
                CoverageConstants.ExtensionLevel => CoverageConstants.HasExtensionRelation,
                _ => null
            };

            if (relationType is null)
            {
                return Failure("level must be SECTION or EXTENSION.");
            }

            var items = await _coverageRepository.ListChildrenAsync(
                parentCoverageItemId,
                normalizedLevel!,
                relationType,
                cancellationToken);

            return ServiceResult<IEnumerable<CoverageCatalogItemDto>>.Success(items);
        }

        private static ServiceResult<IEnumerable<CoverageCatalogItemDto>> Failure(string error)
        {
            return ServiceResult<IEnumerable<CoverageCatalogItemDto>>.Failure(
                "Validation failed.",
                [error],
                StatusCodes.Status400BadRequest);
        }

        private static string? Normalize(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
        }
    }
}
