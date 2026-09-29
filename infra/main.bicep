@description('Azure region')
param location string = resourceGroup().location

@description('Unique name of the new VPF Algorithm Bot App Service')
param appName string

@description('Existing Linux App Service plan ID. Use the StockPartsBot plan only after capacity review.')
param appServicePlanId string

@description('Connection string to an Azure SQL database. The app writes only to schema vpfalgo.')
@secure()
param databaseConnectionString string

@description('New Telegram bot token')
@secure()
param telegramBotToken string

@description('New webhook secret')
@secure()
param telegramWebhookSecret string

@description('Admin password supplied as a secure deployment parameter')
@secure()
param adminPassword string

@description('Telegram ID of the first administrator')
param adminBootstrapTelegramId string

resource app 'Microsoft.Web/sites@2023-12-01' = {
  name: appName
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: appServicePlanId
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      appCommandLine: 'dotnet VPFAlgorithmBot.dll'
      alwaysOn: true
      healthCheckPath: '/health'
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      appSettings: [
        { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
        { name: 'Telegram__Mode', value: 'Webhook' }
        { name: 'Telegram__BotToken', value: telegramBotToken }
        { name: 'Telegram__WebhookSecret', value: telegramWebhookSecret }
        { name: 'ConnectionStrings__BotDatabase', value: databaseConnectionString }
        { name: 'Admin__Password', value: adminPassword }
        { name: 'Admin__BootstrapTelegramId', value: adminBootstrapTelegramId }
        { name: 'Admin__KeyPath', value: '/home/data-protection-keys' }
      ]
    }
  }
}

output appUrl string = 'https://${app.properties.defaultHostName}'
output managedIdentityPrincipalId string = app.identity.principalId
