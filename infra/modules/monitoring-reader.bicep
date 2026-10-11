// Grants principals "Monitoring Reader" on one Application Insights resource (deployed into that resource's group).

@description('Name of the Application Insights resource.')
param appInsightsName string

@description('Object ids of the identities that read telemetry (web app and staging slot).')
param principalIds array

var monitoringReaderRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '43d0d8ad-25c7-4714-9337-8ba259a9fe05')

resource component 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
}

resource reader 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in principalIds: {
  name: guid(component.id, principalId, monitoringReaderRoleId)
  scope: component
  properties: {
    roleDefinitionId: monitoringReaderRoleId
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}]
