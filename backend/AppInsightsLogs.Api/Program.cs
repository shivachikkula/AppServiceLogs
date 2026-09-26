using AppInsightsLogs.Api.Data;
using AppInsightsLogs.Api.Options;
using AppInsightsLogs.Api.Services;
using Azure.Core;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AppInsightsOptions>(builder.Configuration.GetSection(AppInsightsOptions.SectionName));
builder.Services.Configure<KeyVaultOptions>(builder.Configuration.GetSection(KeyVaultOptions.SectionName));
builder.Services.Configure<AzureAdB2COptions>(builder.Configuration.GetSection(AzureAdB2COptions.SectionName));

// The API's own Azure identity: reads connection strings from Key Vault and queries Application Insights.
builder.Services.AddSingleton<TokenCredential>(sp =>
    CredentialFactory.Create(sp.GetRequiredService<IOptions<AppInsightsOptions>>().Value, builder.Environment));

builder.Services.AddHttpClient<AppInsightsQueryClient>(client => client.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddScoped<TelemetryService>();
builder.Services.AddScoped<ApplicationCatalog>();
builder.Services.AddSingleton<ISecretProvider, KeyVaultSecretProvider>();
builder.Services.AddMemoryCache();

builder.Services.AddDbContext<LogViewerDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("LogViewerDb"),
        sql => sql.EnableRetryOnFailure()));

// Users sign in with Azure AD B2C; the API validates the access token the Angular app sends.
var b2c = builder.Configuration.GetSection(AzureAdB2COptions.SectionName).Get<AzureAdB2COptions>() ?? new AzureAdB2COptions();
if (b2c.IsConfigured)
{
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = b2c.MetadataAuthority;
            options.Audience = b2c.EffectiveApiClientId;
            options.MapInboundClaims = false;
            options.TokenValidationParameters.NameClaimType = "name";
        });

    builder.Services.AddAuthorization(options =>
    {
        var policy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme).RequireAuthenticatedUser();
        if (!string.IsNullOrWhiteSpace(b2c.RequiredScope))
        {
            policy.RequireAssertion(ctx => (ctx.User.FindFirst("scp")?.Value ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains(b2c.RequiredScope, StringComparer.OrdinalIgnoreCase));
        }
        options.DefaultPolicy = policy.Build();
    });
}
else if (builder.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(b2c.DevelopmentUserEmail))
{
    builder.Services
        .AddAuthentication(DevelopmentAuthHandler.SchemeName)
        .AddScheme<DevelopmentAuthHandler.SchemeOptions, DevelopmentAuthHandler>(
            DevelopmentAuthHandler.SchemeName, o => o.Email = b2c.DevelopmentUserEmail.Trim().ToLowerInvariant());
    builder.Services.AddAuthorization();
}
else
{
    throw new InvalidOperationException(
        "Azure AD B2C is not configured. Set AzureAdB2C:Instance, Domain, SignUpSignInPolicyId and SpaClientId " +
        "(or, in Development only, AzureAdB2C:DevelopmentUserEmail to run without sign-in).");
}

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

if (!b2c.IsConfigured)
{
    app.Logger.LogWarning("Azure AD B2C is not configured: every request is signed in as {Email} (Development only).", b2c.DevelopmentUserEmail);
}

// Translate unexpected failures into ProblemDetails the UI can display.
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, title, detail) = error switch
    {
        AppInsightsQueryException q => ((int)q.StatusCode, q.Code ?? "Application Insights query failed", q.Message),
        TaskCanceledException or HttpRequestException => (StatusCodes.Status504GatewayTimeout, "Application Insights unreachable", error.Message),
        _ => (StatusCodes.Status500InternalServerError, "Unexpected error", "An unexpected error occurred."),
    };

    context.Response.StatusCode = status;
    await Results.Problem(title: title, detail: detail, statusCode: status).ExecuteAsync(context);
}));

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();

// Serves the compiled Angular app (copied to wwwroot on publish) alongside the API. The SPA itself is public;
// every API controller except /api/auth-config requires a signed-in user.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
