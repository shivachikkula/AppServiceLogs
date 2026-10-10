# OSSE Log Viewer

A web app that reads telemetry from **Azure Application Insights** for several applications and shows it on two pages. Users sign in with **Azure AD B2C**. Each user sees only the applications assigned to their email address.

| Page | What it shows |
| --- | --- |
| **Live logs** (`/live`) | Traces, requests, dependencies, exceptions and custom events. The page polls every 5–60 s and adds newly ingested items at the top. You can filter by type, severity, text, role name and operation id, and pause or clear the view. Click a row to see its details. |
| **Exceptions** (`/exceptions`) | Totals, the top problems grouped by `problemId`, and a list of individual exceptions. Click an exception to open its stack trace and custom dimensions, or jump to all telemetry for the same operation. |
| **Query** (`/query`) | Write your own KQL for the selected application. The editor has **IntelliSense**: completions for your tables and columns, KQL operators and functions, syntax highlighting, error squiggles and hover docs. It also has a table and column explorer, sample queries, a time range, results with CSV export, and an optional **AI assistant** that writes or fixes queries from plain English. |

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

## Query page (KQL)

**IntelliSense.** The editor is Microsoft's Monaco editor with the official Kusto language service (`@kusto/monaco-kusto`), the same one the Azure portal uses. When you pick an application, the API reads its table and column schema with `getschema` (cached for `Query:SchemaCacheMinutes`). The editor then completes and validates against your real columns, including custom ones. Press **Ctrl+Space** for suggestions and **Ctrl+Enter** (or Shift+Enter) to run. The editor loads only on this page (about 1.1 MB, plus a 0.9 MB gzipped language-service worker), so the other pages stay fast.

**Safety.** Queries run with the API's Azure identity, which can usually read *every* application in the catalog. Each query is therefore checked before it runs (`KqlGuard`). Queries that call cross-resource or external functions (`app()`, `workspace()`, `resource()`, `cluster()`, `database()`, `externaldata`), plugins such as `evaluate http_request` or `python`, or management commands are rejected. Mentions inside strings and comments are allowed. Results are capped at `Query:MaxResultRows` (the total count is still shown), and the time range at `Query:MaxRangeDays`. For the strongest isolation, give the API's identity access only to the Application Insights resources that are in `UserApplications`.

**Audit log.** Every query run on the Query page and every AI assistant request is written to the `QueryAudit` table. Each row records who, when, which application, the KQL or the description, the time range, whether it succeeded, the row count, the duration and any error. Denied attempts are included, such as an application the user isn't assigned or a blocked function. If writing the audit row fails, the error is logged and the query still runs. The API's database user therefore needs `db_datawriter` as well as `db_datareader` (see the end of `schema.sql`). Rows are never deleted by the app; schedule a purge if you need a retention limit.

**AI assistant (optional).** Set `AzureOpenAI:Endpoint` and `AzureOpenAI:Deployment` (any chat model deployment, e.g. `gpt-4o-mini`) and an **✦ AI** box appears on the Query page:
- *Write query*: describe what you want in English. Tick **Change the current query** to modify the query already in the editor.
- *Fix with AI*: when a query fails, sends the query and the error message and puts the corrected query in the editor.

The generated query is never run automatically. It goes into the editor with a short explanation, so you can review it first, and it passes the same safety check when run. Only the request, the current query, the error message and the table/column names are sent to the model, **never telemetry data**. Authentication uses `AzureOpenAI:ApiKey` if set; otherwise the API's own identity, which needs the **Cognitive Services OpenAI User** role on the Azure OpenAI resource. Azure OpenAI is billed per token. A typical request with the schema is a few thousand tokens, which costs fractions of a cent with a mini model.

## Admin page

People whose email is listed in `Administration:AdminEmails` see an **Admin** link. In App Service, set the app settings `Administration__AdminEmails__0`, `Administration__AdminEmails__1`, and so on. The API enforces this on every `/api/admin` endpoint. The page has four tabs:

| Tab | What it does |
| --- | --- |
| **Access** | Grant a user access to an application (email + app key), or remove it. Typing an existing app key reuses that application's name; a new app key registers a new application. This replaces hand-written `INSERT`s into `UserApplications`. |
| **Applications** | Rename an application for everyone, and **Check** it: the API reads its Key Vault secret again, takes the ApplicationId and runs a small query. That proves the secret, the connection string and the API's *Monitoring Reader* access in one click. |
| **Audit log** | Every Query-page query and AI request, filterable by user, application and "failed or denied only". Click a row to see the full query and error. |
| **Diagnostics** | Go-live checks run with the API's own identity and settings: B2C settings, administrators, database tables, Key Vault access, and the Azure token for Application Insights and Azure OpenAI. |

The admin page writes to `UserApplications`, so the API's database user needs `db_datawriter` (see `schema.sql`).

## Go-live checklist

Run through this once in each environment, such as test and production:

1. Run `database/schema.sql`. Grant the App Service identity `db_datareader` and `db_datawriter`.
2. Set the app settings: `ConnectionStrings__LogViewerDb`, `AzureAdB2C__*`, `KeyVault__VaultUri`, `ApplicationInsights__Credential=ManagedIdentity`, `Administration__AdminEmails__0`, and optionally `AzureOpenAI__*`. In App Service, set **Health check** to `/healthz`.
3. Grant the App Service identity **Key Vault Secrets User** on the vault, and **Monitoring Reader** on each Application Insights resource you'll add.
4. Sign in as an admin, go to **Admin → Diagnostics** and run the checks. Fix anything red; yellow items are optional.
5. On **Access**, grant yourself an application. On **Applications**, press **Check** on it and wait for the green ✓.
6. Smoke test as a normal user (not an admin):
   - The dropdown lists only that user's applications.
   - **Live logs** shows new items within a few minutes.
   - **Exceptions** opens a stack trace.
   - **Query** completes a column name after Ctrl+Space, runs a query and exports CSV.
   - A query with `app("…")` is rejected.
7. Back on **Admin → Audit log**, confirm the queries from step 6 appear, including the rejected one.

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
| Azure SQL database | `db_datareader` and `db_datawriter` (see the end of `schema.sql`), or use SQL authentication in the connection string |

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
"Query": {
  "MaxResultRows": 10000,  // rows returned to the browser
  "MaxRangeDays": 90,
  "SchemaCacheMinutes": 60
},
"AzureOpenAI": {           // optional: enables the AI assistant on the Query page
  "Endpoint": "",          // https://<resource>.openai.azure.com/
  "Deployment": "",        // chat model deployment name
  "ApiKey": "",            // empty = use the API's Azure identity
  "ApiVersion": "2024-10-21"
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

## CI/CD and code scanning (GitHub Actions)

| Workflow | Runs on | What it does |
| --- | --- | --- |
| [`ci.yml`](.github/workflows/ci.yml) | every PR, pushes to `main` | **API**: build with .NET code analyzers (warnings fail the build), unit tests, vulnerable NuGet package check. **Client**: ESLint (angular-eslint, incl. accessibility rules), production build, `npm audit`. |
| [`codeql.yml`](.github/workflows/codeql.yml) | PRs to `main`, pushes to `main`, weekly | **CodeQL** security analysis (`security-extended` queries) for C# and TypeScript. Findings appear under *Security → Code scanning*. |
| [`dependency-review.yml`](.github/workflows/dependency-review.yml) | every PR | Blocks PRs that add npm or NuGet packages with high or critical vulnerabilities. |
| [`dependabot.yml`](.github/dependabot.yml) | weekly | PRs to update npm, NuGet and GitHub Actions dependencies. |
| [`deploy.yml`](.github/workflows/deploy.yml) | pushes to `main`, manual | Tests, then `dotnet publish` (which also builds the Angular app into `wwwroot`) and deploys the package to **one App Service**. |

All of these tools are free for public repositories. Also turn on **Settings → Code security**: *Dependabot alerts*, *Dependabot security updates*, *Secret scanning* and *Push protection* (free for public repos). Make `main` the **default branch** (Settings → General), because CodeQL's weekly scan and Dependabot run against the default branch.

### Deployment setup

1. **Create the App Service**: Linux, runtime stack **.NET 10**. Enable its system-assigned managed identity, grant the roles in [Permissions](#4-permissions-for-the-apis-identity), and add the app settings from [Configuration](#configuration), e.g. `AzureAdB2C__SpaClientId` and `KeyVault__VaultUri`.
2. In GitHub, go to **Settings → Secrets and variables → Actions** and add the **variable** `AZURE_WEBAPP_NAME` (the App Service name).
3. Choose how the workflow signs in to Azure:
   - **Option A: OpenID Connect (recommended, no stored password).** Create an app registration or user-assigned managed identity. Add a *federated credential* for GitHub Actions: repository `shivachikkula/AppServiceLogs`, entity *Environment*, name `production`. Give it the **Website Contributor** role on the App Service. Then add the variables `AZURE_CLIENT_ID`, `AZURE_TENANT_ID` and `AZURE_SUBSCRIPTION_ID`.
   - **Option B: publish profile (no Entra ID permissions needed).** In the App Service, turn on *Configuration → General settings → SCM Basic Auth Publishing Credentials*, then *Download publish profile*. Store the file's contents as the **secret** `AZURE_WEBAPP_PUBLISH_PROFILE`. The workflow uses this option when `AZURE_CLIENT_ID` is not set.
4. Optionally add required reviewers to the `production` environment (Settings → Environments), so each deployment waits for approval.

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
| `GET /api/query/schema?appKey=` | Tables and columns of the application, for IntelliSense |
| `POST /api/query/run` `{ appKey, query, rangeMinutes }` | Runs user-written KQL (after the safety check). Returns typed columns, rows, total row count and duration |
| `POST /api/query/generate` `{ appKey, prompt, currentQuery?, error? }` | AI assistant: writes or fixes a query (404 when not configured) |
| `GET /api/features` | `{ aiQueryEnabled }` |
| `GET /healthz` | Anonymous health check (database), for App Service *Health check* |
| `GET/POST /api/admin/grants`, `DELETE /api/admin/grants/{id}` | Admin: list, create and remove access grants |
| `GET /api/admin/applications`, `PUT /api/admin/applications/{appKey}`, `POST …/{appKey}/check` | Admin: list, rename and check applications |
| `GET /api/admin/audit?email=&appKey=&failedOnly=&beforeId=&take=` | Admin: query audit log, newest first |
| `GET /api/admin/diagnostics` | Admin: go-live checks |

The API returns **403** for an `appKey` that isn't assigned to the signed-in user. Before any user input goes into a KQL query, it is escaped as a string literal. Numeric inputs and time ranges are clamped.

## Troubleshooting

In Development the 401 response body contains the reason; in other environments it is in the API log (*Rejected B2C access token*).

| Message | Fix |
| --- | --- |
| **401** with *IDX10214: Audience validation failed* | The token's `aud` (open it at [jwt.ms](https://jwt.ms)) must equal `SpaClientId` or `ApiClientId`. With a separate API app registration, set `ApiClientId` to the **API app's client id**. |
| **401** with *IDX10205: Issuer validation failed* or *IDX10501: Signature validation failed* | The token came from a different B2C tenant or user flow. `Instance`, `Domain` and `SignUpSignInPolicyId` must match the `iss` claim and the `tfp`/`acr` claim in the token. |
| **401** with *IDX20803: Unable to obtain configuration* | The API can't download `{Instance}/{Domain}/{Policy}/v2.0/.well-known/openid-configuration`. Check those three settings (no trailing path in `Instance`) and outbound network access. |
| **401** with *IDX10223: Lifetime validation failed* | The token expired. Sign out and in again, and check the machine's clock. |
| *'app()' is not allowed* (Query page) | Queries can only read the selected application. Select the other application in the header instead. |
| Query page: no completions for your columns | The schema request failed (shown above the table list). The editor still completes KQL keywords and functions. |
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
