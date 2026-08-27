namespace IRISBackend.Services
{
    public static class ContractTypeNames
    {
        public const string Facultative = "Facultative";
        public const string TreatyProportionalQuotaShare = "TreatyProportionalQuotaShare";
        public const string TreatyProportionalSurplus = "TreatyProportionalSurplus";
        public const string TreatyNonProportional = "TreatyNonProportional";

        public static readonly string[] All =
        [
            Facultative,
            TreatyProportionalQuotaShare,
            TreatyProportionalSurplus,
            TreatyNonProportional
        ];

        public static string? NormalizeFilter(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Trim().Replace("_", string.Empty, StringComparison.Ordinal).ToUpperInvariant() switch
            {
                "FACULTATIVE" => Facultative,
                "QUOTASHARE" or "TREATYPROPORTIONALQUOTASHARE" => TreatyProportionalQuotaShare,
                "SURPLUS" or "TREATYPROPORTIONALSURPLUS" => TreatyProportionalSurplus,
                "NONPROPORTIONAL" or "TREATYNONPROPORTIONAL" => TreatyNonProportional,
                _ => value.Trim()
            };
        }
    }
}
