// OSSE Log Viewer: one App Service (API + Angular UI) with a staging slot, its own monitoring, Key Vault access,
// Azure SQL and role assignments, all with managed identities (no secrets). Deploy per environment:
//   az deployment group create -g <rg> -f infra/main.bicep -p infra/main.example.bicepparam

targetScope = 'resourceGroup'

@description('Base name, e.g. osse-logviewer-prod. Used for every resource name.')
@minLength(3)
@maxLength(40)
param name string

param location string = resourceGroup().location

@description('App Service plan SKU. Deployment slots need Standard (S1) or higher; P0v3 is the smallest Premium v3.')
param appServiceSku string = 'P0v3'

@description('Create a "staging" slot for zero-downtime deployments (deploy → health check → swap).')
param useStagingSlot bool = true

// ---- Azure AD B2C (user sign-in) ----
param b2cInstance string
param b2cDomain string
param b2cSignUpSignInPolicyId string
param b2cSpaClientId string
param b2cApiClientId string = ''
param b2cApiScopes array = []
param b2cRequiredScope string = ''

@description('Emails of the people who can open the Admin page.')
param adminEmails array

// ---- Key Vault holding one secret per application (the Application Insights connection strings) ----
@description('Create a new Key Vault. Set false to use an existing vault in this resource group.')
param createKeyVault bool = true
@description('Key Vault name (3-24 characters). Defaults to a name derived from "name".')
param keyVaultName string = take(replace('${name}-kv', '_', '-'), 24)

// ---- Azure SQL (UserApplications + QueryAudit) with Microsoft Entra-only authentication ----
@description('Display name of the Microsoft Entra group that administers the SQL server (include the GitHub deployment identity so the pipeline can run database/schema.sql).')
param sqlAdminGroupName string
@description('Object id of that group.')
param sqlAdminGroupObjectId string
param sqlDatabaseSku object = {
  name: 'Basic'
  tier: 'Basic'
}

@description('Resource ids of the Application Insights resources users may view; the app gets Monitoring Reader on each.')
param monitoredAppInsightsIds array = []

// ---- Optional AI query assistant ----
param azureOpenAIEndpoint string = ''
param azureOpenAIDeployment string = ''

@description('Email address for alerts (5xx errors, failed health checks, slow responses). Empty: no alerts.')
param alertEmail string = ''

var tags = {
  application: 'osse-log-viewer'
  environment: name
}
var keyVaultSecretsUserRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
var sqlServerName = '${name}-sql'
var sqlDatabaseName = 'logviewer'

// ---------------------------------------------------------------------------------------------------------------
// Monitoring of the viewer itself
// ---------------------------------------------------------------------------------------------------------------
resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${name}-logs'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${name}-ai'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
  }
}

// ---------------------------------------------------------------------------------------------------------------
// Key Vault (RBAC authorization; the app reads secrets with its managed identity)
// ---------------------------------------------------------------------------------------------------------------
resource newVault 'Microsoft.KeyVault/vaults@2023-07-01' = if (createKeyVault) {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    enablePurgeProtection: true
    publicNetworkAccess: 'Enabled'
  }
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
  dependsOn: [ newVault ]
}

// ---------------------------------------------------------------------------------------------------------------
// Azure SQL
// ---------------------------------------------------------------------------------------------------------------
resource sqlServer 'Microsoft.Sql/servers@2023-08-01' = {
  name: sqlServerName
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: sqlAdminGroupName
      sid: sqlAdminGroupObjectId
      principalType: 'Group'
      tenantId: subscription().tenantId
    }
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  tags: tags
  sku: sqlDatabaseSku
}

// Lets Azure services (the web app) reach the server. The deployment pipeline adds a temporary rule for its runner.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// ---------------------------------------------------------------------------------------------------------------
// App Service (API + UI) and staging slot
// ---------------------------------------------------------------------------------------------------------------
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${name}-plan'
  location: location
  tags: tags
  kind: 'linux'
  sku: { name: appServiceSku }
  properties: { reserved: true }
}

var adminSettings = [for (email, i) in adminEmails: {
  name: 'Administration__AdminEmails__${i}'
  value: email
}]
var scopeSettings = [for (scope, i) in b2cApiScopes: {
  name: 'AzureAdB2C__ApiScopes__${i}'
  value: scope
}]
var appSettings = concat([
  { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
  { name: 'AzureAdB2C__Instance', value: b2cInstance }
  { name: 'AzureAdB2C__Domain', value: b2cDomain }
  { name: 'AzureAdB2C__SignUpSignInPolicyId', value: b2cSignUpSignInPolicyId }
  { name: 'AzureAdB2C__SpaClientId', value: b2cSpaClientId }
  { name: 'AzureAdB2C__ApiClientId', value: b2cApiClientId }
  { name: 'AzureAdB2C__RequiredScope', value: b2cRequiredScope }
  { name: 'KeyVault__VaultUri', value: vault.properties.vaultUri }
  { name: 'AzureOpenAI__Endpoint', value: azureOpenAIEndpoint }
  { name: 'AzureOpenAI__Deployment', value: azureOpenAIDeployment }
], adminSettings, scopeSettings)

var siteConfig = {
  linuxFxVersion: 'DOTNETCORE|10.0'
  alwaysOn: true
  http20Enabled: true
  minTlsVersion: '1.2'
  ftpsState: 'Disabled'
  healthCheckPath: '/healthz'
  appSettings: appSettings
  connectionStrings: [
    {
      name: 'LogViewerDb'
      type: 'SQLAzure'
      // The managed identity of whichever slot is running; see database/grant-app-identity.sql.
      connectionString: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${sqlDatabaseName};Authentication=Active Directory Managed Identity;Encrypt=True'
    }
  ]
}

resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: '${name}-app'
  location: location
  tags: tags
  kind: 'app,linux'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: siteConfig
  }
}

resource stagingSlot 'Microsoft.Web/sites/slots@2023-12-01' = if (useStagingSlot) {
  parent: webApp
  name: 'staging'
  location: location
  tags: tags
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: siteConfig
  }
}

// Deployments use OpenID Connect from GitHub Actions, so basic-auth publishing credentials stay off.
resource scmBasicAuth 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2023-12-01' = {
  parent: webApp
  name: 'scm'
  properties: { allow: false }
}

resource ftpBasicAuth 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2023-12-01' = {
  parent: webApp
  name: 'ftp'
  properties: { allow: false }
}

var appPrincipalIds = concat([ webApp.identity.principalId ], useStagingSlot ? [ stagingSlot!.identity.principalId ] : [])

// ---------------------------------------------------------------------------------------------------------------
// Role assignments: read Key Vault secrets, read each monitored Application Insights resource
// ---------------------------------------------------------------------------------------------------------------
resource keyVaultSecretsUserApp 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, keyVaultSecretsUserRoleId, webApp.id)
  scope: vault
  properties: {
    roleDefinitionId: keyVaultSecretsUserRoleId
    principalId: webApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource keyVaultSecretsUserSlot 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (useStagingSlot) {
  name: guid(vault.id, keyVaultSecretsUserRoleId, '${webApp.id}/slots/staging')
  scope: vault
  properties: {
    roleDefinitionId: keyVaultSecretsUserRoleId
    principalId: stagingSlot!.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

module monitoringReader 'modules/monitoring-reader.bicep' = [for (componentId, i) in monitoredAppInsightsIds: {
  name: 'monitoring-reader-${i}'
  scope: resourceGroup(split(componentId, '/')[2], split(componentId, '/')[4])
  params: {
    appInsightsName: last(split(componentId, '/'))
    principalIds: appPrincipalIds
  }
}]

// ---------------------------------------------------------------------------------------------------------------
// Alerts
// ---------------------------------------------------------------------------------------------------------------
resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = if (!empty(alertEmail)) {
  name: '${name}-alerts'
  location: 'global'
  tags: tags
  properties: {
    groupShortName: take('osselogs', 12)
    enabled: true
    emailReceivers: [
      { name: 'owner', emailAddress: alertEmail, useCommonAlertSchema: true }
    ]
  }
}

var alertRules = [
  { name: 'http-5xx', description: 'More than 10 server errors (HTTP 5xx) in 5 minutes', metric: 'Http5xx', aggregation: 'Total', operator: 'GreaterThan', threshold: 10, severity: 1 }
  { name: 'health-check', description: 'Health check (/healthz) below 100% for 5 minutes', metric: 'HealthCheckStatus', aggregation: 'Average', operator: 'LessThan', threshold: 100, severity: 1 }
  { name: 'slow-responses', description: 'Average response time above 5 seconds for 5 minutes', metric: 'HttpResponseTime', aggregation: 'Average', operator: 'GreaterThan', threshold: 5, severity: 2 }
]

resource alerts 'Microsoft.Insights/metricAlerts@2018-03-01' = [for rule in alertRules: if (!empty(alertEmail)) {
  name: '${name}-${rule.name}'
  location: 'global'
  tags: tags
  properties: {
    description: rule.description
    severity: rule.severity
    enabled: true
    scopes: [ webApp.id ]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          criterionType: 'StaticThresholdCriterion'
          name: rule.metric
          metricName: rule.metric
          timeAggregation: rule.aggregation
          operator: rule.operator
          threshold: rule.threshold
        }
      ]
    }
    actions: [ { actionGroupId: actionGroup.id } ]
  }
}]

output webAppName string = webApp.name
output webAppUrl string = 'https://${webApp.properties.defaultHostName}'
output stagingUrl string = useStagingSlot ? 'https://${stagingSlot!.properties.defaultHostName}' : ''
output keyVaultUri string = vault.properties.vaultUri
output sqlServerName string = sqlServer.name
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlDatabaseName string = sqlDatabaseName
output appInsightsName string = appInsights.name
