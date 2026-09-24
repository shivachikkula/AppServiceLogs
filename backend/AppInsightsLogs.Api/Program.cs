using AppInsightsLogs.Api.Options;
using AppInsightsLogs.Api.Services;
using Azure.Core;
using Azure.Identity;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AppInsightsOptions>(builder.Configuration.GetSection(AppInsightsOptions.SectionName));

// Reading telemetry requires Microsoft Entra ID: the connection string only grants ingestion (write) access.
builder.Services.AddSingleton<TokenCredential>(sp =>
{
    var options = sp.GetRequiredService<IOptions<AppInsightsOptions>>().Value;
    return options.UsesServicePrincipal
        ? new ClientSecretCredential(options.TenantId, options.ClientId, options.ClientSecret)
        : new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            // Allows a user-assigned managed identity to be selected via AZURE_CLIENT_ID.
            ManagedIdentityClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID"),
        });
});

builder.Services.AddHttpClient<AppInsightsQueryClient>(client => client.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddScoped<TelemetryService>();

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// Translate Application Insights failures into ProblemDetails the UI can display.
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

// Serves the compiled Angular app (copied to wwwroot on publish) alongside the API.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
