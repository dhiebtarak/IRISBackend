namespace IRISBackend.Services;

public interface IContractUmrGenerator
{
    Task<ServiceResult<string>> GenerateAsync(
        string contractType,
        DateOnly inceptionDate,
        CoverageSelectionPlan coverageSelection,
        CancellationToken cancellationToken = default);
}
