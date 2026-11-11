namespace IRISBackend.Models;

public sealed class CounterpartyContact
{
    public int Id { get; set; }
    public int CounterpartyId { get; set; }
    public int SortOrder { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public Counterparty Counterparty { get; set; } = null!;
}

public partial class Counterparty
{
    public ICollection<CounterpartyContact> AdditionalContacts { get; set; } = new List<CounterpartyContact>();
}
