namespace IRISBackend.Services
{
    public static class CoverageConstants
    {
        public const string MainCoverLevel = "MAIN_COVER";
        public const string SectionLevel = "SECTION";
        public const string ExtensionLevel = "EXTENSION";

        public const string HasSectionRelation = "HAS_SECTION";
        public const string HasExtensionRelation = "HAS_EXTENSION";

        public static bool IsMainCoverLevel(string? value)
        {
            return Is(value, MainCoverLevel) || Is(value, "MAIN");
        }

        public static bool IsSectionRelation(string? value)
        {
            return Is(value, HasSectionRelation) || Is(value, SectionLevel);
        }

        public static bool IsExtensionRelation(string? value)
        {
            return Is(value, HasExtensionRelation) || Is(value, ExtensionLevel);
        }

        private static bool Is(string? actual, string expected)
        {
            return string.Equals(actual?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
        }
    }
}
