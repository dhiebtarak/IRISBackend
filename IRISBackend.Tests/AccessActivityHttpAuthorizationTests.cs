using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using IRISBackend.Models;
using IRISBackend.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace IRISBackend.Tests;
public sealed class AccessActivityHttpAuthorizationTests
{
    [Fact]
    public async Task EmployeeReceives403AndRevokedSessionReceives401()
    {
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName="Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var key=new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('t',64)));
        builder.Services.AddDistributedMemoryCache(); builder.Services.AddHttpContextAccessor();
        builder.Services.AddDbContext<IRISBackendContext>(o=>o.UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=NeverConnected;Integrated Security=true"));
        builder.Services.AddScoped<AuthSessionService>();
        builder.Services.AddScoped<ILoginAuditService>(_=>null!);
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o=>
        {
            o.MapInboundClaims=false;
            o.TokenValidationParameters=new TokenValidationParameters {ValidateAudience=false,ValidateIssuer=false,ValidateIssuerSigningKey=true,IssuerSigningKey=key,ClockSkew=TimeSpan.Zero};
            o.Events=new JwtBearerEvents {OnTokenValidated=async c=>
            {
                var service=c.HttpContext.RequestServices.GetRequiredService<AuthSessionService>();
                if(!await service.ValidateAsync(c.Principal!.FindFirstValue("sid"),c.Principal!.FindFirstValue(ClaimTypes.NameIdentifier)!,false,default)) c.Fail("Session unavailable.");
            }};
        });
        builder.Services.AddAuthorization();
        await using var app=builder.Build();app.UseAuthentication();app.UseAuthorization();
        app.MapGet("/protected",()=>"ok").RequireAuthorization();
        app.MapGet("/api/admin/access-activity/test",()=>"ok").RequireAuthorization(p=>p.RequireRole("Admin"));
        await app.StartAsync();
        try
        {
            var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client=new HttpClient { BaseAddress=new Uri(address) };
            var sid=new string('A',64);
            var cache=app.Services.GetRequiredService<IDistributedCache>();
            await cache.SetStringAsync("iris:session:"+sid,"employee");
            var handler=new JsonWebTokenHandler();
            var token=handler.CreateToken(new SecurityTokenDescriptor {Subject=new ClaimsIdentity([new System.Security.Claims.Claim(ClaimTypes.NameIdentifier,"employee"),new System.Security.Claims.Claim(ClaimTypes.Role,"Employee"),new System.Security.Claims.Claim("sid",sid)]),Expires=DateTime.UtcNow.AddMinutes(10),SigningCredentials=new SigningCredentials(key,SecurityAlgorithms.HmacSha256)});
            client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);
            Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/protected")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("/api/admin/access-activity/test")).StatusCode);
            await cache.SetStringAsync("iris:session:"+sid+":terminal","Revoked");
            Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/protected")).StatusCode);
        }
        finally {await app.StopAsync();}
    }
}
