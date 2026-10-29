using IRISBackend.DTOs;
using IRISBackend.Middleware;
using IRISBackend.Models;
using IRISBackend.Repositories;
using IRISBackend.Services;
using IRISBackend.Validators;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Reflection;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddControllersWithViews()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            if (context.HttpContext.Request.Path.StartsWithSegments("/api/contracts/facultative"))
            {
                var invalidKeys = context.ModelState
                    .Where(entry => entry.Value?.Errors.Count > 0)
                    .Select(entry => NormalizeContractModelStateKey(entry.Key))
                    .Distinct()
                    .ToArray();
                context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("FacultativeContractModelBinding")
                    .LogWarning("Facultative contract request rejected during model binding. TraceId={TraceId}, Fields={Fields}",
                        context.HttpContext.TraceIdentifier, string.Join(",", invalidKeys));

                var fieldErrors = context.ModelState
                    .Where(entry => entry.Value?.Errors.Count > 0)
                    .SelectMany(entry => entry.Value!.Errors.Select(error => new ContractFieldError(
                        NormalizeContractModelStateKey(entry.Key), error.ErrorMessage)));

                return new BadRequestObjectResult(ContractValidationErrorResponseDto.From(fieldErrors));
            }

            var errors = context.ModelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .SelectMany(entry => entry.Value!.Errors.Select(error =>
                {
                    var fieldName = NormalizeModelStateKey(entry.Key);
                    return string.IsNullOrWhiteSpace(fieldName)
                        ? error.ErrorMessage
                        : $"{fieldName}: {error.ErrorMessage}";
                }))
                .Where(message => !string.IsNullOrWhiteSpace(message));

            return new BadRequestObjectResult(new ApiErrorResponseDto
            {
                Message = "Validation failed.",
                Errors = errors
            });
        };
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Description = "Enter the JWT token returned by /api/auth/login. Swagger will send it as: Authorization: Bearer {token}"
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document, null)] = []
    });

    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
    }
});
builder.Services.AddAutoMapper(_ => { }, typeof(Program).Assembly);
builder.Services.AddValidatorsFromAssemblyContaining<CounterpartyCreateDtoValidator>();
builder.Services.Configure<CounterpartyAttachmentStorageOptions>(
    builder.Configuration.GetSection(CounterpartyAttachmentStorageOptions.SectionName));
builder.Services.Configure<DocumentStorageOptions>(
    builder.Configuration.GetSection(DocumentStorageOptions.SectionName));
builder.Services.AddOptions<IrisInvoiceBrandingOptions>()
    .Bind(builder.Configuration.GetSection(IrisInvoiceBrandingOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.CompanyName), "IrisInvoiceBranding:CompanyName is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.AddressLine1), "IrisInvoiceBranding:AddressLine1 is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.AddressLine2), "IrisInvoiceBranding:AddressLine2 is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Phone), "IrisInvoiceBranding:Phone is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.TaxIdentifier), "IrisInvoiceBranding:TaxIdentifier is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.CommercialRegister), "IrisInvoiceBranding:CommercialRegister is required.")
    .ValidateOnStart();
builder.Services
    .AddOptions<IrisBankAccountsOptions>()
    .Bind(builder.Configuration.GetSection("IrisBankAccounts"));

builder.Services.AddDbContext<IRISBackendContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DevDB")));
builder.Services.AddDbContext<AppIdentityContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DevDB")));

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;

    var knownProxy = builder.Configuration["ForwardedHeaders:KnownProxy"];
    if (IPAddress.TryParse(knownProxy, out var proxyAddress))
    {
        options.KnownProxies.Add(proxyAddress);
    }
});

var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    Directory.CreateDirectory(dataProtectionKeysPath);
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequireDigit = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<AppIdentityContext>()
    .AddDefaultTokenProviders();

var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];
var jwtSigningKey = builder.Configuration["Jwt:SigningKey"];

if (string.IsNullOrWhiteSpace(jwtIssuer) ||
    string.IsNullOrWhiteSpace(jwtAudience) ||
    string.IsNullOrWhiteSpace(jwtSigningKey))
{
    throw new InvalidOperationException("JWT settings are not configured.");
}

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IWorkspaceAccessService, WorkspaceAccessService>();
builder.Services.AddScoped<ILoginAuditService, LoginAuditService>();
builder.Services.AddScoped<ICounterpartyService, CounterpartyService>();
builder.Services.AddScoped<ICounterpartyAttachmentStorageService, CounterpartyAttachmentStorageService>();
builder.Services.AddScoped<IDocumentStorageService, DocumentStorageService>();
builder.Services.AddScoped<IDocumentAttachmentService, DocumentAttachmentService>();
builder.Services.AddScoped<IReferenceDataService, ReferenceDataService>();
builder.Services.AddScoped<IFacultativeContractService, FacultativeContractService>();
builder.Services.AddScoped<IContractUmrGenerator, ContractUmrGenerator>();
builder.Services.AddScoped<IContractReadService, ContractReadService>();
builder.Services.AddScoped<IContractPlacementService, ContractPlacementService>();
builder.Services.AddSingleton<PremiumCalculationService>();
builder.Services.AddScoped<FacultativePanelFinancialRepairService>();
builder.Services.AddScoped<ICoverageCatalogService, CoverageCatalogService>();
builder.Services.AddScoped<ICoverageSelectionService, CoverageSelectionService>();
builder.Services.AddScoped<IFinancialWorkflowService, FinancialWorkflowService>();
builder.Services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
builder.Services.AddScoped<IDocumentAttachmentRepository, DocumentAttachmentRepository>();
builder.Services.AddScoped<ILoginAuditRepository, LoginAuditRepository>();
builder.Services.AddScoped<ICounterpartyRepository, CounterpartyRepository>();
builder.Services.AddScoped<IReferenceDataRepository, ReferenceDataRepository>();
builder.Services.AddScoped<IContractRepository, ContractRepository>();
builder.Services.AddScoped<IContractPlacementRepository, ContractPlacementRepository>();
builder.Services.AddScoped<ICoverageRepository, CoverageRepository>();
builder.Services.AddHttpContextAccessor();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .WithExposedHeaders(
                  "Content-Disposition",
                  "X-Financial-Note-Type",
                  "X-Financial-Note-Reference",
                  "X-PDF-Template-Version");
    });
});

var app = builder.Build();

var irisBankAccountOptions = app.Services
    .GetRequiredService<Microsoft.Extensions.Options.IOptions<IrisBankAccountsOptions>>()
    .Value;
var configuredIrisBankAccounts = irisBankAccountOptions.Accounts ?? [];
var activeIrisBankAccounts = configuredIrisBankAccounts.Where(account => account.IsActive).ToList();
app.Logger.LogInformation(
    "IRIS bank-account configuration: Environment={Environment}, Section={Section}, SectionExists={SectionExists}, ConfiguredAccounts={ConfiguredAccounts}, ActiveAccounts={ActiveAccounts}, AccountMetadata={AccountMetadata}",
    app.Environment.EnvironmentName,
    IrisBankAccountsOptions.SectionName,
    builder.Configuration.GetSection(IrisBankAccountsOptions.SectionName).Exists(),
    configuredIrisBankAccounts.Count,
    activeIrisBankAccounts.Count,
    string.Join("; ", activeIrisBankAccounts.Select((account, index) =>
        $"Currency={account.Currency},DisplayOrder={account.DisplayOrder ?? index}," +
        $"AccountHolderPresent={!string.IsNullOrWhiteSpace(account.AccountHolder)}," +
        $"BankNamePresent={!string.IsNullOrWhiteSpace(account.BankName)}," +
        $"RoutingPresent={!string.IsNullOrWhiteSpace(account.Rib) || !string.IsNullOrWhiteSpace(account.Iban)}," +
        $"SwiftBicPresent={!string.IsNullOrWhiteSpace(account.SwiftBic)}")));

if (args.Contains("--bootstrap-admin", StringComparer.OrdinalIgnoreCase))
{
    await BootstrapDeploymentAdminAsync(app.Services);
    return;
}

app.UseForwardedHeaders();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

var enableHttpsRedirection = builder.Configuration.GetValue("Security:EnableHttpsRedirection", true);
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else if (enableHttpsRedirection)
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseStaticFiles();

app.UseRouting();
app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }))
    .AllowAnonymous();
app.MapGet("/health/ready", async (IRISBackendContext db, CancellationToken cancellationToken) =>
{
    if (await db.Database.CanConnectAsync(cancellationToken))
    {
        return (IResult)Results.Ok(new { status = "Ready" });
    }

    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
}).AllowAnonymous();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

static string NormalizeModelStateKey(string key)
{
    if (string.IsNullOrWhiteSpace(key))
    {
        return string.Empty;
    }

    var lastSegment = key[(key.LastIndexOf('.') + 1)..];
    return lastSegment.Length == 0
        ? string.Empty
        : char.ToLowerInvariant(lastSegment[0]) + lastSegment[1..];
}

static string NormalizeContractModelStateKey(string key)
{
    if (string.IsNullOrWhiteSpace(key)) return "request";
    var path = key.Replace("$.", string.Empty, StringComparison.Ordinal).TrimStart('.');
    if (path.StartsWith("request.", StringComparison.OrdinalIgnoreCase)) path = path[8..];
    var normalized = string.Join('.', path.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(segment =>
        segment.Length == 0 ? segment : char.ToLowerInvariant(segment[0]) + segment[1..]));
    return string.IsNullOrWhiteSpace(normalized) ? "request" : normalized;
}

static async Task BootstrapDeploymentAdminAsync(IServiceProvider services)
{
    var email = Environment.GetEnvironmentVariable("IRIS_BOOTSTRAP_ADMIN_EMAIL");
    var password = Environment.GetEnvironmentVariable("IRIS_BOOTSTRAP_ADMIN_PASSWORD");
    var fullName = Environment.GetEnvironmentVariable("IRIS_BOOTSTRAP_ADMIN_FULL_NAME");

    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(fullName))
    {
        throw new InvalidOperationException(
            "Admin bootstrap requires IRIS_BOOTSTRAP_ADMIN_EMAIL, IRIS_BOOTSTRAP_ADMIN_PASSWORD and IRIS_BOOTSTRAP_ADMIN_FULL_NAME.");
    }

    await IdentityRoleSeeder.SeedAsync(services);
    using var scope = services.CreateScope();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var user = await userManager.FindByEmailAsync(email);
    if (user is null)
    {
        user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = fullName,
            Department = "Reassurance",
            Position = "Administrator"
        };

        var createResult = await userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Admin bootstrap could not create the identity account: {string.Join("; ", createResult.Errors.Select(error => error.Code))}");
        }
    }

    if (!await userManager.IsInRoleAsync(user, IdentityRoles.Admin))
    {
        var roleResult = await userManager.AddToRoleAsync(user, IdentityRoles.Admin);
        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Admin bootstrap could not assign the Admin role: {string.Join("; ", roleResult.Errors.Select(error => error.Code))}");
        }
    }

    Console.WriteLine("Deployment administrator is ready. Remove bootstrap credentials from the process environment.");
}
