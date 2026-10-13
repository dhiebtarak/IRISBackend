namespace IRISBackend.Services;

public sealed class IrisInvoiceBrandingOptions
{
    public const string SectionName = "IrisInvoiceBranding";

    public string CompanyName { get; set; } = string.Empty;
    public string AddressLine1 { get; set; } = string.Empty;
    public string AddressLine2 { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string TaxIdentifier { get; set; } = string.Empty;
    public string CommercialRegister { get; set; } = string.Empty;
}
