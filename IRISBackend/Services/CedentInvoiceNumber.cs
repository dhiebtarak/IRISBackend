namespace IRISBackend.Services;

public static class CedentInvoiceNumber
{
    public static string Format(int year, int sequence)
    {
        if (sequence is < 1 or > 999999)
            throw new ArgumentOutOfRangeException(nameof(sequence), "The annual invoice sequence must be between 1 and 999999.");

        return $"INV-{year}-{sequence:000000}";
    }
}
