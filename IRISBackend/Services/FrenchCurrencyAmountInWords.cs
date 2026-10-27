namespace IRISBackend.Services;

internal static class FrenchCurrencyAmountInWords
{
    private static readonly string[] Units =
    [
        "", "mille", "million", "milliard", "billion", "billiard", "trillion"
    ];

    public static string Convert(decimal amount, string currencyCode, int decimalPlaces)
    {
        decimalPlaces = Math.Clamp(decimalPlaces, 0, 6);
        var rounded = decimal.Round(Math.Abs(amount), decimalPlaces, MidpointRounding.AwayFromZero);
        var integerPart = decimal.Truncate(rounded);
        var scale = Pow10(decimalPlaces);
        var minorPart = decimal.ToInt64((rounded - integerPart) * scale);
        var majorNumber = decimal.ToInt64(integerPart);
        var currency = GetCurrency(currencyCode, decimalPlaces);

        var words = ToWords(majorNumber);
        words += majorNumber == 1 ? $" {currency.MajorSingular}" : $" {currency.MajorPlural}";

        if (minorPart > 0)
        {
            words += $" et {ToWords(minorPart)} ";
            words += minorPart == 1 ? currency.MinorSingular : currency.MinorPlural;
        }

        return words;
    }

    private static (string MajorSingular, string MajorPlural, string MinorSingular, string MinorPlural)
        GetCurrency(string code, int decimalPlaces) => code.Trim().ToUpperInvariant() switch
        {
            "TND" => ("dinar tunisien", "dinars tunisiens", "millime", "millimes"),
            "EUR" => ("euro", "euros", "centime", "centimes"),
            "USD" => ("dollar am\u00e9ricain", "dollars am\u00e9ricains", "cent", "cents"),
            _ => ($"unit\u00e9 {code.Trim()}", $"unit\u00e9s {code.Trim()}",
                decimalPlaces == 3 ? "milli\u00e8me" : "centime",
                decimalPlaces == 3 ? "milli\u00e8mes" : "centimes")
        };

    private static decimal Pow10(int power)
    {
        var result = 1m;
        for (var index = 0; index < power; index++) result *= 10m;
        return result;
    }

    private static string ToWords(long value)
    {
        if (value == 0) return "z\u00e9ro";
        if (value < 0) return $"moins {ToWords(-value)}";

        var parts = new List<string>();
        var groupIndex = 0;
        while (value > 0)
        {
            var group = (int)(value % 1000);
            if (group > 0)
            {
                var groupText = UnderOneThousand(group, usePluralHundred: groupIndex != 1);
                if (groupIndex > 0)
                {
                    if (groupIndex == 1 && group == 1)
                        groupText = "mille";
                    else
                        groupText += $" {Units[groupIndex]}" +
                            (groupIndex > 1 && group > 1 ? "s" : string.Empty);
                }

                parts.Insert(0, groupText);
            }

            value /= 1000;
            groupIndex++;
            if (groupIndex >= Units.Length && value > 0)
                throw new ArgumentOutOfRangeException(nameof(value), "The amount is too large to spell in French.");
        }

        return string.Join(" ", parts);
    }

    private static string UnderOneThousand(int value, bool usePluralHundred)
    {
        var hundreds = value / 100;
        var remainder = value % 100;
        var result = hundreds switch
        {
            0 => string.Empty,
            1 => "cent",
            _ => $"{UnderOneHundred(hundreds)} cent" + (remainder == 0 && usePluralHundred ? "s" : string.Empty)
        };

        if (remainder > 0)
            result = string.IsNullOrEmpty(result) ? UnderOneHundred(remainder) : $"{result} {UnderOneHundred(remainder)}";

        return result;
    }

    private static string UnderOneHundred(int value)
    {
        if (value < 17) return new[]
        {
            "z\u00e9ro", "un", "deux", "trois", "quatre", "cinq", "six", "sept", "huit", "neuf",
            "dix", "onze", "douze", "treize", "quatorze", "quinze", "seize"
        }[value];

        if (value < 20) return $"dix-{UnderOneHundred(value - 10)}";
        if (value < 70)
        {
            var tens = value / 10;
            var remainder = value % 10;
            var tensWord = new[] { "", "", "vingt", "trente", "quarante", "cinquante", "soixante" }[tens];
            if (remainder == 0) return tensWord;
            if (remainder == 1) return $"{tensWord} et un";
            return $"{tensWord}-{UnderOneHundred(remainder)}";
        }

        if (value < 80)
        {
            if (value == 71) return "soixante et onze";
            return $"soixante-{UnderOneHundred(value - 60)}";
        }

        if (value < 100)
        {
            if (value == 80) return "quatre-vingts";
            if (value == 81) return "quatre-vingt-un";
            return $"quatre-vingt-{UnderOneHundred(value - 80)}";
        }

        throw new ArgumentOutOfRangeException(nameof(value));
    }
}
