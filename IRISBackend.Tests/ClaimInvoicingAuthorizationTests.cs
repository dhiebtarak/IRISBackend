using System.Net;
using System.Net.Http.Json;
using IRISBackend.DTOs;
using System.Security.Claims;
using System.Text.Encodings.Web;
using IRISBackend.Controllers;
using IRISBackend.Models;
using IRISBackend.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace IRISBackend.Tests;

public sealed partial class OutgoingRemittanceIntegrationTests
{
    [Fact]
    public async Task ClaimInvoicingHttpAuthorizationReturns401And403()
    {
        var c = await ReadyClaimInvoicing();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(ClaimsController).Assembly);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IFinancialWorkflowService>(sp => new FinancialWorkflowService(_db, sp.GetRequiredService<IHttpContextAccessor>(),
            NullLogger<FinancialWorkflowService>.Instance, Options.Create(new IrisInvoiceBrandingOptions()), irisBankAccounts: Options.Create(TestIrisBankAccounts())));
        builder.Services.AddAuthentication("ClaimTest").AddScheme<AuthenticationSchemeOptions, ClaimTestAuthentication>("ClaimTest", _ => { });
        builder.Services.AddAuthorization(o =>
        {
            o.AddPolicy("Module:BackOffice", p => p.RequireAuthenticatedUser().RequireClaim(ModuleAccessCatalog.ClaimType, ModuleAccessCatalog.BackOffice));
            o.AddPolicy("Module:Claims", p => p.RequireAuthenticatedUser().RequireClaim(ModuleAccessCatalog.ClaimType, ModuleAccessCatalog.Claims));
        });
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/claims")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsync($"/api/claims/{c.ClaimId}/invoicing/approve", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"))).StatusCode);
        http.DefaultRequestHeaders.Add("X-Test-Module", ModuleAccessCatalog.Claims);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsync($"/api/claims/{c.ClaimId}/invoicing/approve", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync($"/api/claims/{c.ClaimId}/invoicing/preview")).StatusCode);
        http.DefaultRequestHeaders.Remove("X-Test-Module"); http.DefaultRequestHeaders.Add("X-Test-Module", ModuleAccessCatalog.BackOffice);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync($"/api/claims/{c.ClaimId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsync($"/api/claims/contracts/{c.ContractId}", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"))).StatusCode);
        var preview = await http.GetFromJsonAsync<ClaimInvoicingPreviewDto>($"/api/claims/{c.ClaimId}/invoicing/preview");
        var decision = new { label = "HTTP claim settlement", version = preview!.Claim.Version,
            allocations = preview.Panels.Select(p => new { panelId = p.PanelId, amount = p.AmountText }).ToArray() };
        var approved = await http.PostAsJsonAsync($"/api/claims/{c.ClaimId}/invoicing/approve", decision);
        Assert.Equal(HttpStatusCode.Created, approved.StatusCode);
        var bundle = await http.GetAsync($"/api/claims/{c.ClaimId}/invoicing/documents?kind=bundle");
        Assert.Equal(HttpStatusCode.OK, bundle.StatusCode);Assert.Equal("application/zip", bundle.Content.Headers.ContentType!.MediaType);
        Assert.Equal(HttpStatusCode.OK,(await http.PostAsJsonAsync($"/api/claims/{c.ClaimId}/invoicing/approve", decision)).StatusCode);
        await app.StopAsync();
    }
    private sealed class ClaimTestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var module = Request.Headers["X-Test-Module"].ToString();
            if (string.IsNullOrEmpty(module)) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new System.Security.Claims.Claim(ClaimTypes.NameIdentifier, "back-user"), new System.Security.Claims.Claim(ModuleAccessCatalog.ClaimType, module)], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
