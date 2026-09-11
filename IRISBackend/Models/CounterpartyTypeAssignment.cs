namespace IRISBackend.Models;

public sealed class CounterpartyTypeAssignment
{
    public int CounterpartyId { get; set; }

    public string Type { get; set; } = string.Empty;

    public Counterparty Counterparty { get; set; } = null!;
}
