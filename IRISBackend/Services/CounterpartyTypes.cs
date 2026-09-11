namespace IRISBackend.Services;

public static class CounterpartyTypes
{
    public const string Cedent = "Cedent";
    public const string Reinsurer = "Reinsurer";
    public const string Client = "Client";
    public const string Partner = "Partner";

    public static readonly string[] All = [Cedent, Reinsurer, Client, Partner];

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        normalized = All.FirstOrDefault(type =>
            string.Equals(type, value.Trim(), StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        return normalized.Length > 0;
    }
}
