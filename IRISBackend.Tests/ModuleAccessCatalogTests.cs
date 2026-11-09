using IRISBackend.Services;
using Xunit;

namespace IRISBackend.Tests;

public sealed class ModuleAccessCatalogTests
{
    [Fact]
    public void CatalogueContainsDistinctFinanceWorkflowsAndCounterpartyAccounts()
    {
        Assert.Contains(ModuleAccessCatalog.Encaissement, ModuleAccessCatalog.All);
        Assert.Contains(ModuleAccessCatalog.Decaissement, ModuleAccessCatalog.All);
        Assert.Contains(ModuleAccessCatalog.CounterpartyBankAccounts, ModuleAccessCatalog.All);
        Assert.Equal(ModuleAccessCatalog.All.Count, ModuleAccessCatalog.All.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("Front officer", "Counterparties", "Contracts", "Claims")]
    [InlineData("Back officer", "BackOffice", null, null)]
    [InlineData("Finance officer", "Encaissement", "Decaissement", "CounterpartyBankAccounts")]
    public void ExistingUsersReceivePositionMatchingAccessUntilAdminConfiguresModules(
        string position, string first, string? second, string? third)
    {
        var modules = ModuleAccessCatalog.LegacyModules("Reassurance", position);
        Assert.Contains(first, modules);
        if (second is not null) Assert.Contains(second, modules);
        if (third is not null) Assert.Contains(third, modules);
    }

    [Fact]
    public void UnsupportedModuleIsRejectedAndMatchingIsCaseInsensitive()
    {
        Assert.False(ModuleAccessCatalog.IsValid("Admin"));
        Assert.True(ModuleAccessCatalog.IsValid("encaissement"));
        Assert.Equal(ModuleAccessCatalog.Encaissement, ModuleAccessCatalog.Canonicalize("encaissement"));
    }
}
