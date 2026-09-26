# OSSE Application Log Viewer

A web app that reads telemetry from **Azure Application Insights** for several applications and shows it on two pages. Users sign in with **Azure AD B2C**. Each user sees only the applications assigned to their email address.

| Page | What it shows |
| --- | --- |
| **Live logs** (`/live`) | Traces, requests, dependencies, exceptions and custom events. The page polls every 5–60 s and adds newly ingested items at the top. You can filter by type, severity, text, role name and operation id, and pause or clear the view. Click a row to see its details. |
| **Exceptions** (`/exceptions`) | Totals, the top problems grouped by `problemId`, and a list of individual exceptions. Click an exception to open its stack trace and custom dimensions, or jump to all telemetry for the same operation. |

The **Application** dropdown in the header lists the applications assigned to the signed-in user. Both pages show data for the selected application. The last selection is remembered per user.

```
frontend/   Angular 21 SPA (standalone components, signals, MSAL.js for B2C sign-in)
backend/    ASP.NET Core Web API on .NET 10 + unit tests
database/   SQL script for the UserApplications table
```

## How it works

```
Browser ──(1) sign in──▶ Azure AD B2C
   │  (2) GET /api/applications  + B2C access token
   ▼
API ──(3) SELECT … FROM UserApplications WHERE Email = <email claim>──▶ Azure SQL
   │  (4) user picks an application → GET /api/logs/live?appKey=orders-api
   │  (5) checks the (email, appKey) row exists, otherwise 403
   │  (6) reads the Key Vault secret named <appKey> = the app's connection string
   │  (7) takes the ApplicationId from the connection string
   └──(8) queries Application Insights with the API's own Entra ID identity
```

- **The connection string never reaches the browser.** The browser only knows application names and app keys. The API looks up the secret itself, but only for an app key that is assigned to the signed-in user.
- **The connection string only identifies the resource.** Azure does not accept an Application Insights connection string for *reading* data. The API reads its `ApplicationId` and then queries `https://api.applicationinsights.io` with its own Entra ID identity (a managed identity in Azure, or `az login` locally).
- Connection strings are cached in memory for `KeyVault:CacheMinutes` (default 10), so Key Vault isn't called on every refresh.

## Azure setup

### 1. Azure AD B2C (user sign-in)

In your existing B2C tenant:

1. **Register the SPA.** Go to *App registrations → New registration*. Set the platform to **Single-page application**, with redirect URI `http://localhost:4200/` for local development plus your App Service URL, e.g. `https://<app>.azurewebsites.net/`.
2. **Expose an API.** The simplest setup uses the same app registration for the SPA and the API. The SPA then requests its own client id as the scope, and nothing else is needed. If you'd rather have a separate API registration, create one, add a scope under *Expose an API* (e.g. `Logs.Read`), grant the SPA permission to it, and set `ApiClientId`, `ApiScopes` and `RequiredScope` (below).
3. **User flow.** In your sign-up/sign-in user flow (e.g. `B2C_1_signupsignin`), go to *Application claims* and tick **Email Addresses**. The API needs the email claim to find the user's applications.

### 2. Azure SQL database

Run [`database/schema.sql`](database/schema.sql) to create the `UserApplications` table, then add one row per user and application:

```sql
INSERT INTO dbo.UserApplications (Email, ApplicationName, AppKey) VALUES
    (N'jane.doe@contoso.com', N'Orders API',   N'orders-api-appinsights'),
    (N'jane.doe@contoso.com', N'Payments API', N'payments-api-appinsights');
```

| Column | Meaning |
| --- | --- |
| `Email` | The user's B2C email address, in lower case |
| `ApplicationName` | Name shown in the dropdown |
| `AppKey` | Name of the Key Vault secret holding that application's connection string (letters, digits and dashes) |

### 3. Key Vault

For each application, create a secret named after its `AppKey`. Its value is the application's full Application Insights **connection string** (Application Insights → Overview → Connection String). It must include the `ApplicationId=` part.

```bash
az keyvault secret set --vault-name <vault> --name orders-api-appinsights --value "InstrumentationKey=...;ApplicationId=..."
```

### 4. Permissions for the API's identity

The API uses one Azure identity: the App Service's managed identity in Azure, or your `az login` account locally. That identity needs:

| Resource | Role |
| --- | --- |
| Key Vault | **Key Vault Secrets User** |
| Each Application Insights resource | **Monitoring Reader** |
| Azure SQL database | `db_datareader` (see the end of `schema.sql`), or use SQL authentication in the connection string |

Users don't need any Azure roles. They only sign in with B2C.

## Configuration

`backend/AppInsightsLogs.Api/appsettings.json`:

```jsonc
"ConnectionStrings": {
  // Managed identity / az login:
  "LogViewerDb": "Server=tcp:<server>.database.windows.net;Database=<db>;Authentication=Active Directory Default;Encrypt=True"
},
"AzureAdB2C": {
  "Instance": "https://<tenant>.b2clogin.com",
  "Domain": "<tenant>.onmicrosoft.com",
  "SignUpSignInPolicyId": "B2C_1_signupsignin",
  "SpaClientId": "<SPA app registration client id>",
  "ApiClientId": "",       // only with a separate API registration (token audience)
  "ApiScopes": [],         // e.g. ["https://<tenant>.onmicrosoft.com/<api>/Logs.Read"]
  "RequiredScope": ""      // e.g. "Logs.Read"
},
"KeyVault": {
  "VaultUri": "https://<vault>.vault.azure.net/",
  "CacheMinutes": 10
},
"ApplicationInsights": {
  "TenantId": "",          // Azure (Entra ID) tenant of Key Vault / App Insights — not the B2C tenant
  "Credential": "Default", // Default | AzureCli | VisualStudio | ManagedIdentity
  "ManagedIdentityClientId": "",
  "ClientId": "", "ClientSecret": "",
  "QueryEndpoint": "https://api.applicationinsights.io",
  "MaxRows": 500
}
```

The Angular app has no settings of its own. It loads the B2C settings from `GET /api/auth-config` at startup, so one build works in every environment.

## Run locally

Prerequisites: .NET 10 SDK and Node.js 22+.

```bash
# 1. API on http://localhost:5080
cd backend/AppInsightsLogs.Api
az login --tenant <azure-tenant-id>          # identity used for Key Vault + App Insights
dotnet user-secrets set "ConnectionStrings:LogViewerDb" "<sql connection string>"
dotnet run --launch-profile http

# 2. UI on http://localhost:4200 (proxies /api to the backend)
cd frontend
npm install
npm start
```

**Without B2C (development only):** leave the `AzureAdB2C` settings empty and set `AzureAdB2C:DevelopmentUserEmail` (e.g. in `appsettings.Development.json` or user secrets). Every request is then treated as signed in with that email. This only works when `ASPNETCORE_ENVIRONMENT=Development`. In any other environment the API refuses to start unless B2C is configured.

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

Enable a system-assigned managed identity on the App Service and grant it the roles in [Permissions](#4-permissions-for-the-apis-identity). Set the settings above as App Service application settings, e.g. `AzureAdB2C__SpaClientId`, `KeyVault__VaultUri`, `ConnectionStrings__LogViewerDb` and `ApplicationInsights__Credential=ManagedIdentity`. Add the App Service URL as a redirect URI on the B2C SPA registration.

## API

All endpoints except `/api/auth-config` need a B2C access token.

| Endpoint | Description |
| --- | --- |
| `GET /api/auth-config` | Public B2C settings for the SPA |
| `GET /api/me` | The signed-in user's email and name |
| `GET /api/applications` | `[{ applicationName, appKey }]` for the signed-in user |
| `GET /api/logs/live?appKey=&since=&lookbackMinutes=&types=trace,request&minSeverity=&search=&roleName=&operationId=&take=` | Recent telemetry. Pass the returned `cursor` back as `since` to get only newly ingested items. |
| `GET /api/exceptions?appKey=&rangeMinutes=&search=&problemId=&roleName=&operationId=&take=` | Exception occurrences |
| `GET /api/exceptions/summary?appKey=&rangeMinutes=&search=&roleName=` | Exceptions grouped by problem id |
| `GET /api/exceptions/{itemId}?appKey=&rangeMinutes=` | One exception with its reconstructed stack trace |

The API returns **403** for an `appKey` that isn't assigned to the signed-in user. Before any user input goes into a KQL query, it is escaped as a string literal. Numeric inputs and time ranges are clamped.

## Troubleshooting

| Message | Fix |
| --- | --- |
| *Your sign-in token contains no email address* | Tick **Email Addresses** under the user flow's *Application claims*, then sign out and in again. |
| *No applications are assigned to …* | Add rows for that email (lower case) to `UserApplications`. |
| *Could not read the UserApplications table* | Check `ConnectionStrings:LogViewerDb`, the SQL firewall, and that `schema.sql` has been run. |
| *No Key Vault secret named …* | Create the secret, named exactly like the `AppKey`. |
| *…has no ApplicationId segment* | Store the full connection string from the Application Insights Overview page. |
| *not allowed to read secrets from Key Vault* | Grant the API's identity **Key Vault Secrets User**. |
| *Could not acquire an Azure access token* | Locally: run `az login --tenant <tenant-id>` and set `ApplicationInsights:TenantId` and `"Credential": "AzureCli"`. In Azure: enable the managed identity. Accounts in several tenants often fail with *"failed due to an unhandled exception"* until `TenantId` is set. |
| *…does not have read access to this Application Insights resource* | Grant the API's identity **Monitoring Reader** on that resource. |

## Notes

- **"Real time"**: Application Insights usually has an ingestion delay of 1–3 minutes. The live page tracks `ingestion_time()`, so items that arrive late or out of order are still picked up.
- **Cost**: queries on Analytics-plan log tables are free. The running costs are App Service hosting and the Azure SQL database (the Basic tier is enough for this table).
- To use a sovereign cloud, change `QueryEndpoint` (for example `https://api.applicationinsights.azure.cn`).
