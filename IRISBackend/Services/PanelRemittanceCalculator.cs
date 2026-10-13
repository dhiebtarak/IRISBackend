namespace IRISBackend.Services;

public static class PanelRemittanceCalculator
{
    public static int Precision(int? decimalPlaces) => Math.Clamp(decimalPlaces ?? 2, 0, 4);

    public static bool IsFullyPaired(decimal received, decimal allocated, int precision)
    {
        var unit = Unit(precision);
        return received > 0m && Math.Abs(received - allocated) < unit / 2m;
    }

    public static decimal Unit(int precision) => precision switch
    {
        0 => 1m,
        1 => 0.1m,
        2 => 0.01m,
        3 => 0.001m,
        4 => 0.0001m,
        _ => throw new ArgumentOutOfRangeException(nameof(precision))
    };

    public static IReadOnlyList<decimal> Distribute(decimal amount, IReadOnlyList<decimal> weights,
        IReadOnlyList<decimal> capacities, int precision)
    {
        if (weights.Count == 0 || weights.Count != capacities.Count || weights.Any(x => x < 0m) || weights.Sum() <= 0m)
            throw new OutgoingRemittanceValidationException("Eligible panels must have positive net premium weights.");
        if (amount <= 0m || decimal.Round(amount, precision, MidpointRounding.AwayFromZero) != amount)
            throw new OutgoingRemittanceValidationException("The allocation amount does not match the currency precision.");
        var available = capacities.Select((x, i) => weights[i] == 0m ? 0m : decimal.Floor(Math.Max(0m, x) / Unit(precision)) * Unit(precision)).ToArray();
        if (available.Sum() < amount)
            throw new OutgoingRemittanceValidationException("The allocation exceeds the eligible panels' outstanding payable amounts.");

        var result = new decimal[weights.Count];
        var remaining = amount;
        var pending = Enumerable.Range(0, weights.Count).Where(i => available[i] > 0m).ToList();
        // Cap exhausted panels first, then redistribute the balance among the others.
        while (remaining > 0m)
        {
            var totalWeight = pending.Sum(i => weights[i]);
            var capped = pending.Where(i => remaining * weights[i] / totalWeight > available[i]).ToArray();
            if (capped.Length > 0)
            {
                foreach (var i in capped)
                {
                    result[i] = available[i];
                    remaining -= available[i];
                    pending.Remove(i);
                }
                continue;
            }
            var basis = remaining;
            for (var position = 0; position < pending.Count; position++)
            {
                var i = pending[position];
                var value = position == pending.Count - 1 ? remaining
                    : decimal.Round(basis * weights[i] / totalWeight, precision, MidpointRounding.AwayFromZero);
                value = Math.Min(Math.Min(value, remaining), available[i]);
                result[i] = value;
                remaining -= value;
            }
            // Rounding may leave the last panel at capacity. Assign residual units deterministically.
            for (var position = pending.Count - 1; remaining > 0m && position >= 0; position--)
            {
                var i = pending[position];
                var extra = Math.Min(remaining, available[i] - result[i]);
                result[i] += extra;
                remaining -= extra;
            }
        }
        return result;
    }
}

public sealed class OutgoingRemittanceValidationException(string message) : Exception(message);
