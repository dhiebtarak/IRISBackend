namespace IRISBackend.Models;

public sealed class IrisBankAccount
{
    public int IdIrisBankAccount { get; set; }
    public string BankName { get; set; } = string.Empty;
    public int IdCurrency { get; set; }
    public string Iban { get; set; } = string.Empty;
    public string BicSwift { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public Currency IdCurrencyNavigation { get; set; } = null!;
}
