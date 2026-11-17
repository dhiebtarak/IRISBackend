using System.Net;
using System.Net.Http.Json;
using FluentValidation;
using IRISBackend.Controllers;
using IRISBackend.DTOs;
using IRISBackend.Mappings;
using IRISBackend.Models;
using IRISBackend.Repositories;
using IRISBackend.Services;
using IRISBackend.Validators;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IRISBackend.Tests;

public sealed class CurrencyDeploymentIntegrationTests
{
    [Fact]
    public async Task FcfaDeploymentIsRepeatablePreservesIdsAndFlowsThroughProtectedCurrencyApi()
    {
        var name = "IRIS_Fcfa_Tests_" + Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<IRISBackendContext>().UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={name};Integrated Security=true;TrustServerCertificate=true").Options;
        await using var db = new IRISBackendContext(options);
        await db.Database.EnsureCreatedAsync();
        try
        {
            var original = new Currency { CurrencyCode = "TND", CurrencySymbol = "DT", Name = "Tunisian Dinar", DecimalPlaces = 3, IsDefault = true, IsActive = true };
            db.Currencies.Add(original); await db.SaveChangesAsync();
            var originalId = original.IdCurrency;
            var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Deployment", "20261116_add_fcfa_currency.sql"));
            await db.Database.ExecuteSqlRawAsync(script);
            var fcfaId = await db.Currencies.Where(c => c.CurrencyCode == "FCFA").Select(c => c.IdCurrency).SingleAsync();
            await db.Database.ExecuteSqlRawAsync(script);
            Assert.Equal(2, await db.Currencies.CountAsync());
            Assert.Equal(originalId, (await db.Currencies.AsNoTracking().SingleAsync(c => c.CurrencyCode == "TND")).IdCurrency);
            var fcfa = await db.Currencies.AsNoTracking().SingleAsync(c => c.CurrencyCode == "FCFA");
            Assert.Equal(fcfaId, fcfa.IdCurrency); Assert.False(fcfa.IsDefault); Assert.True(fcfa.IsActive);
            Assert.Equal(0, fcfa.DecimalPlaces); Assert.Equal("FCFA", fcfa.CurrencySymbol); Assert.Equal("West and Central Africa", fcfa.Country);

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddDbContext<IRISBackendContext>(o => o.UseSqlServer(db.Database.GetConnectionString()));
            builder.Services.AddScoped<IReferenceDataRepository, ReferenceDataRepository>();
            builder.Services.AddScoped<IReferenceDataService, ReferenceDataService>();
            builder.Services.AddScoped<IValidator<CedentLookupQueryDto>, CedentLookupQueryDtoValidator>();
            builder.Services.AddAutoMapper(_ => { }, typeof(ContractProfile).Assembly);
            builder.Services.AddControllers().AddApplicationPart(typeof(ReferenceDataController).Assembly);
            builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, FinanceWorkspaceImprovementsTests.FinanceTestAuthenticationHandler>("test", _ => { });
            builder.Services.AddAuthorization(o => o.AddPolicy("Module:ReferenceData", p => p.RequireAuthenticatedUser().RequireAssertion(c => !c.User.IsInRole(IdentityRoles.Admin) && ModuleAccessCatalog.All.Where(m => m != ModuleAccessCatalog.Claims).Any(m => c.User.HasClaim(ModuleAccessCatalog.ClaimType, m)))));
            await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
            try
            {
                using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/reference-data/currencies")).StatusCode);
                client.DefaultRequestHeaders.Add("X-Test-Role", "claims");
                Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/reference-data/currencies")).StatusCode);
                client.DefaultRequestHeaders.Remove("X-Test-Role"); client.DefaultRequestHeaders.Add("X-Test-Role", "front");
                var currencies = (await client.GetFromJsonAsync<CurrencyReferenceDto[]>("/api/reference-data/currencies"))!;
                Assert.Contains(currencies, c => c.Id == originalId && c.Code == "TND" && c.IsDefault);
                Assert.Contains(currencies, c => c.Id == fcfaId && c.Code == "FCFA" && c.DecimalPlaces == 0 && !c.IsDefault);
                Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsJsonAsync("/api/reference-data/currencies", new { code = "FCFA" })).StatusCode);
            }
            finally { await app.StopAsync(); }
            // Existing FCFA defaults are deliberately preserved; duplicate legacy rows require review.
            await db.Currencies.Where(c => c.IdCurrency == fcfaId).ExecuteUpdateAsync(u => u.SetProperty(c => c.IsDefault, true));
            await db.Database.ExecuteSqlRawAsync(script);
            Assert.True((await db.Currencies.AsNoTracking().SingleAsync(c => c.IdCurrency == fcfaId)).IsDefault);
            db.Currencies.Add(new Currency { CurrencyCode = " fcfa ", Name = "Duplicate", IsActive = true }); await db.SaveChangesAsync();
            var error = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => db.Database.ExecuteSqlRawAsync(script));
            Assert.Equal(50001, error.Number);
        }
        finally { await db.Database.EnsureDeletedAsync(); }
    }
}
