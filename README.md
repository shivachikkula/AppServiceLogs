# App Insights Log Viewer

A web app that reads telemetry from **Azure Application Insights** and shows it on two pages:

| Page | What it shows |
| --- | --- |
| **Live logs** (`/live`) | Traces, requests, dependencies, exceptions and custom events. The page polls every 5–60 s and adds newly ingested items at the top. You can filter by type, severity, text, role name and operation id, and pause or clear the view. Click a row to see its details. |
| **Exceptions** (`/exceptions`) | Totals, the top problems grouped by `problemId`, and a list of individual exceptions. Click an exception to open its stack trace and custom dimensions, or jump to all telemetry for the same operation. |

```
frontend/   Angular 21 SPA (standalone components, signals)
backend/    ASP.NET Core Web API on .NET 10 + unit tests
```

## How authentication works

The Application Insights **connection string (or instrumentation key) can only be used to *send* telemetry**. Azure does not accept it for *reading* data, and Microsoft is retiring the older read-only "API keys" in favour of Entra ID. The app therefore works like this:

1. **Connection string → which resource.** The API reads the `ApplicationId=` segment of your connection string to find the resource to query. If your connection string has no `ApplicationId`, copy the *Application ID* from the resource's **Configure → API Access** blade into `ApplicationInsights:ApplicationId`.
2. **Microsoft Entra ID → permission to read.** The API gets a token for `https://api.applicationinsights.io` and calls `POST /v1/apps/{applicationId}/query` with KQL. It gets the token in one of two ways:
   - **Service principal**: set `TenantId`, `ClientId` and `ClientSecret`.
   - **`DefaultAzureCredential`** (when the three values above are empty): a managed identity when running in Azure App Service, or your `az login` / Visual Studio sign-in when running locally.

The identity needs the **Monitoring Reader** (or **Reader**) role on the Application Insights resource.

```bash
# Example: create a service principal with read access to the resource
az ad sp create-for-rbac --name appinsights-log-viewer \
  --role "Monitoring Reader" \
  --scopes /subscriptions/<sub-id>/resourceGroups/<rg>/providers/microsoft.insights/components/<app-insights-name>
```

## Configuration

`backend/AppInsightsLogs.Api/appsettings.json`:

```json
"ApplicationInsights": {
  "ConnectionString": "InstrumentationKey=...;IngestionEndpoint=...;ApplicationId=...",
  "ApplicationId": "",          // optional override
  "TenantId": "",               // tenant of the App Insights resource (recommended)
  "Credential": "Default",      // Default | AzureCli | VisualStudio | ManagedIdentity
  "ManagedIdentityClientId": "",// user-assigned managed identity (optional)
  "ClientId": "",               // optional service principal (with TenantId)
  "ClientSecret": "",
  "QueryEndpoint": "https://api.applicationinsights.io",
  "MaxRows": 500
}
```

Don't commit secrets. Locally, use user secrets:

```bash
cd backend/AppInsightsLogs.Api
dotnet user-secrets set "ApplicationInsights:ConnectionString" "<connection string>"
dotnet user-secrets set "ApplicationInsights:ClientSecret" "<secret>"   # only for a service principal
```

In Azure App Service, use application settings (for example `ApplicationInsights__ConnectionString`), preferably together with a managed identity instead of a client secret.

## Troubleshooting: "Could not acquire an Azure access token"

The API needs an Entra ID token before it can query Application Insights. If it can't get one, the page shows the error, including the underlying reason from Azure.Identity. The most common fixes:

1. **Sign in with an account that can read the resource.** Run `az login --tenant <tenant-id>` and set `"Credential": "AzureCli"`. For Visual Studio, sign in under *Tools → Options → Azure Service Authentication* and set `"Credential": "VisualStudio"`.
2. **Set `TenantId`** to the tenant that owns the Application Insights resource (Azure portal → Microsoft Entra ID → Overview → Tenant ID). An account that belongs to several tenants (for example, a personal Microsoft account plus a work directory) otherwise gets a token for the wrong tenant, and `DefaultAzureCredential` reports *"failed due to an unhandled exception"*.
3. **Grant the role.** The signed-in identity needs **Monitoring Reader** on the Application Insights resource.
4. **Or use a service principal** (`TenantId` + `ClientId` + `ClientSecret`). See the `az ad sp create-for-rbac` command above.

In the Development environment, managed identity is skipped, because it only exists when running inside Azure.

## Run locally

Prerequisites: .NET 10 SDK and Node.js 22+.

```bash
# 1. API on http://localhost:5080
cd backend/AppInsightsLogs.Api
az login                      # if you are not using a service principal
dotnet run --launch-profile http

# 2. UI on http://localhost:4200 (proxies /api to the backend)
cd frontend
npm install
npm start
```

Tests:

```bash
cd backend && dotnet test
```

## Deploy (single App Service)

`dotnet publish` also builds the Angular app and copies it into `wwwroot`, so one App Service hosts both the UI and the API:

```bash
cd backend/AppInsightsLogs.Api
dotnet publish -c Release -o ./publish      # add -p:SkipFrontend=true to publish the API only
```

Enable a system-assigned managed identity on the App Service, give it **Monitoring Reader** on the Application Insights resource, and set `ApplicationInsights__ConnectionString`.

## API

| Endpoint | Description |
| --- | --- |
| `GET /api/status` | Whether the API is configured, the resolved application id, and the authentication mode |
| `GET /api/logs/live?since=&lookbackMinutes=&types=trace,request&minSeverity=&search=&roleName=&operationId=&take=` | Recent telemetry. Pass the returned `cursor` back as `since` to get only newly ingested items. |
| `GET /api/exceptions?rangeMinutes=&search=&problemId=&roleName=&operationId=&take=` | Exception occurrences |
| `GET /api/exceptions/summary?rangeMinutes=&search=&roleName=` | Exceptions grouped by problem id |
| `GET /api/exceptions/{itemId}?rangeMinutes=` | One exception with its reconstructed stack trace |

Before any user input goes into a KQL query, it is escaped as a string literal. Numeric inputs and time ranges are clamped.

## Notes

- **"Real time"**: Application Insights usually has an ingestion delay of 1–3 minutes. The live page tracks `ingestion_time()`, so items that arrive late or out of order are still picked up. Live Metrics has no public API for this.
- To use a sovereign cloud, change `QueryEndpoint` (for example `https://api.applicationinsights.azure.cn`).
