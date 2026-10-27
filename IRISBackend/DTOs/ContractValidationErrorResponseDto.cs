namespace IRISBackend.DTOs;

public sealed class ContractValidationErrorResponseDto
{
    public string Message { get; init; } = "Contract validation failed.";
    public IDictionary<string, string[]> Errors { get; init; } = new Dictionary<string, string[]>();

    public static ContractValidationErrorResponseDto From(IEnumerable<ContractFieldError> messages)
    {
        var errors = messages
            .Where(item => !string.IsNullOrWhiteSpace(item.Message))
            .GroupBy(item => NormalizeField(item.Field, item.Message))
            .ToDictionary(group => group.Key, group => group.Select(item => item.Message).Distinct().ToArray());
        return new ContractValidationErrorResponseDto { Errors = errors };
    }

    public static string NormalizeField(string? field, string message)
    {
        if (string.IsNullOrWhiteSpace(field)) return InferField(message);
        var path = field.Replace("$.", string.Empty, StringComparison.Ordinal).TrimStart('.');
        if (path.StartsWith("request.", StringComparison.OrdinalIgnoreCase)) path = path[8..];
        return string.Join('.', path.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(segment =>
            char.ToLowerInvariant(segment[0]) + segment[1..]));
    }

    public static string InferField(string message)
    {
        if (message.StartsWith("name:", StringComparison.OrdinalIgnoreCase) || message.Contains("contract name", StringComparison.OrdinalIgnoreCase)) return "name";
        if (message.StartsWith("counterpartyId:", StringComparison.OrdinalIgnoreCase)) return "counterpartyId";
        if (message.StartsWith("currencyId:", StringComparison.OrdinalIgnoreCase)) return "currencyId";
        if (message.StartsWith("inceptionDate:", StringComparison.OrdinalIgnoreCase)) return "inceptionDate";
        if (message.StartsWith("expiryDate:", StringComparison.OrdinalIgnoreCase)) return "expiryDate";
        if (message.StartsWith("frontingReference:", StringComparison.OrdinalIgnoreCase)) return "frontingReference";
        if (message.StartsWith("isFronting:", StringComparison.OrdinalIgnoreCase)) return "isFronting";
        if (message.StartsWith("writtenSharePct:", StringComparison.OrdinalIgnoreCase)) return "writtenSharePct";
        if (message.StartsWith("signedSharePct:", StringComparison.OrdinalIgnoreCase)) return "signedSharePct";
        if (message.StartsWith("cedentCommissionPct:", StringComparison.OrdinalIgnoreCase)) return "cedentCommissionPct";
        if (message.StartsWith("sumInsured:", StringComparison.OrdinalIgnoreCase)) return "sumInsured";
        if (message.StartsWith("premiumRate:", StringComparison.OrdinalIgnoreCase)) return "premiumRate";
        if (message.StartsWith("coverageSelection:", StringComparison.OrdinalIgnoreCase)) return "coverageSelection";
        if (message.Contains("counterparty", StringComparison.OrdinalIgnoreCase) || message.Contains("cedent", StringComparison.OrdinalIgnoreCase)) return "counterpartyId";
        if (message.Contains("currency", StringComparison.OrdinalIgnoreCase)) return "currencyId";
        if (message.Contains("coverage", StringComparison.OrdinalIgnoreCase) || message.Contains("section", StringComparison.OrdinalIgnoreCase) || message.Contains("extension", StringComparison.OrdinalIgnoreCase)) return "coverageSelection";
        if (message.Contains("expiryDate", StringComparison.OrdinalIgnoreCase)) return "expiryDate";
        if (message.Contains("inceptionDate", StringComparison.OrdinalIgnoreCase)) return "inceptionDate";
        if (message.Contains("premiumRate", StringComparison.OrdinalIgnoreCase)) return "premiumRate";
        if (message.Contains("sumInsured", StringComparison.OrdinalIgnoreCase) || message.Contains("gross premium", StringComparison.OrdinalIgnoreCase)) return "sumInsured";
        if (message.Contains("commission", StringComparison.OrdinalIgnoreCase)) return "cedentCommissionPct";
        if (message.Contains("deductible", StringComparison.OrdinalIgnoreCase)) return "deductible";
        return "contract";
    }
}

public sealed record ContractFieldError(string? Field, string Message);
