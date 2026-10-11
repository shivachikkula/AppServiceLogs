// Example parameters. Copy per environment (e.g. main.prod.bicepparam) and fill in your values.
using 'main.bicep'

param name = 'osse-logviewer-prod'
param appServiceSku = 'P0v3'
param useStagingSlot = true

param b2cInstance = 'https://dcgovb2cosse.b2clogin.com'
param b2cDomain = 'dcgovb2cosse.onmicrosoft.com'
param b2cSignUpSignInPolicyId = 'B2C_1A_SIGNUP_SIGNIN'
param b2cSpaClientId = '<SPA app registration client id>'
param b2cApiClientId = '<API app registration client id>'
param b2cApiScopes = [
  'https://dcgovb2cosse.onmicrosoft.com/<api-client-id>/access_as_user'
]
param b2cRequiredScope = 'access_as_user'

param adminEmails = [
  'you@example.com'
]

param createKeyVault = true

param sqlAdminGroupName = 'osse-logviewer-sql-admins'
param sqlAdminGroupObjectId = '<object id of that Entra group>'

param monitoredAppInsightsIds = [
  '/subscriptions/<sub>/resourceGroups/<rg>/providers/microsoft.insights/components/<orders-api-appinsights>'
]

param alertEmail = 'team@example.com'
