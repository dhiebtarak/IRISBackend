using IRISBackend.Repositories;

namespace IRISBackend.Services;

public sealed class ContractUmrGenerator : IContractUmrGenerator
{
    private const string OrganizationPrefix = "IT";
    private const int MaximumSequence = 9999;

    private readonly IContractRepository _contractRepository;

    public ContractUmrGenerator(IContractRepository contractRepository)
    {
        _contractRepository = contractRepository;
    }

    public async Task<ServiceResult<string>> GenerateAsync(
        string contractType,
        DateOnly inceptionDate,
        CoverageSelectionPlan coverageSelection,
        CancellationToken cancellationToken = default)
    {
        if (!ContractTypeCodes.TryGetValue(contractType.Trim(), out var contractTypeCode))
        {
            return ServiceResult<string>.Failure(
                $"No UMR code is configured for contract type '{contractType}'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var mainCoverCode = coverageSelection.MainCoverCode.Trim().ToUpperInvariant();
        if (mainCoverCode.Length == 0 ||
            mainCoverCode.Length > 50 ||
            mainCoverCode.Any(character => !char.IsLetterOrDigit(character)))
        {
            return ServiceResult<string>.Failure(
                "The selected main cover has an invalid coverage code.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var sequence = await _contractRepository.AllocateUmrSequenceAsync(
            inceptionDate.Year,
            contractTypeCode,
            mainCoverCode,
            cancellationToken);

        if (sequence > MaximumSequence)
        {
            return ServiceResult<string>.Failure(
                $"The UMR sequence for {inceptionDate.Year}, {contractTypeCode}, and {mainCoverCode} has exceeded 9999.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return ServiceResult<string>.Success(
            $"{OrganizationPrefix}{inceptionDate:yy}-{contractTypeCode}-{mainCoverCode}-{sequence:0000}");
    }

    public static IReadOnlyDictionary<string, string> ContractTypeCodes { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ContractConstants.FacultativeType] = "F"
        };
}
