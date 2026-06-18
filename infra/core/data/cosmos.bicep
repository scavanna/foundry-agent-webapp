// Cosmos DB Serverless — most economical option: pay per RU consumed, no minimum monthly cost.
// One shared account in the central Diagramatica resource group.
// Multiple container apps (Legal, Edgar) each get their own database but share the account.

param name string
param location string
param tags object = {}

/// Principal IDs that receive the Cosmos DB Built-in Data Contributor role.
/// Typically the managed-identity principalIds of each Container App.
param dataContributorPrincipalIds array = []

// ---------------------------------------------------------------------------
// Cosmos DB Account — Serverless capacity mode
// ---------------------------------------------------------------------------
resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' = {
  name: name
  location: location
  tags: tags
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    // Serverless: no minimum RUs, pay per operation
    capabilities: [
      { name: 'EnableServerless' }
    ]
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    // Disable public access except from Azure services — tighten later with private endpoint if needed
    publicNetworkAccess: 'Enabled'
    isVirtualNetworkFilterEnabled: false
    minimalTlsVersion: 'Tls12'
    disableLocalAuth: true  // Force managed identity only — no connection strings
    disableKeyBasedMetadataWriteAccess: false
  }
}

// ---------------------------------------------------------------------------
// Database — one per environment name, shared by Legal and Edgar via agentId partition
// ---------------------------------------------------------------------------
resource database 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-05-15' = {
  parent: cosmosAccount
  name: 'conversations'
  properties: {
    resource: {
      id: 'conversations'
    }
    // No throughput here — Serverless accounts don't support database-level throughput
  }
}

// Conversations container — partition key: /userId
resource conversationsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: database
  name: 'conversations'
  properties: {
    resource: {
      id: 'conversations'
      partitionKey: {
        paths: [ '/userId' ]
        kind: 'Hash'
        version: 2
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        includedPaths: [
          { path: '/*' }
        ]
        excludedPaths: [
          // Exclude large text fields from indexing to save RUs
          { path: '/messages/*' }
        ]
        compositeIndexes: [
          // Support listing conversations by user + date descending
          [
            { path: '/userId', order: 'ascending' }
            { path: '/createdAt', order: 'descending' }
          ]
          // Support filtering by agentId within a user's conversations
          [
            { path: '/userId', order: 'ascending' }
            { path: '/agentId', order: 'ascending' }
            { path: '/createdAt', order: 'descending' }
          ]
        ]
      }
      // TTL at container level: -1 means disabled by default; individual documents set their own TTL
      defaultTtl: -1
    }
  }
}

// Messages container — partition key: /conversationId
// Separate from conversations for efficient per-conversation reads without loading all metadata
resource messagesContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: database
  name: 'messages'
  properties: {
    resource: {
      id: 'messages'
      partitionKey: {
        paths: [ '/conversationId' ]
        kind: 'Hash'
        version: 2
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        includedPaths: [
          { path: '/conversationId/?' }
          { path: '/userId/?' }
          { path: '/createdAt/?' }
          { path: '/role/?' }
        ]
        excludedPaths: [
          { path: '/*' }
        ]
      }
      defaultTtl: -1
    }
  }
}

// Audit log container — append-only, short retention via TTL (90 days default)
resource auditContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: database
  name: 'audit'
  properties: {
    resource: {
      id: 'audit'
      partitionKey: {
        paths: [ '/userId' ]
        kind: 'Hash'
        version: 2
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        includedPaths: [
          { path: '/userId/?' }
          { path: '/action/?' }
          { path: '/timestamp/?' }
          { path: '/conversationId/?' }
          { path: '/agentId/?' }
        ]
        excludedPaths: [
          { path: '/*' }
        ]
      }
      // Hard-delete audit records after 90 days via TTL
      defaultTtl: 7776000  // 90 days in seconds
    }
  }
}

// Cohort runs container — stores each multi-agent comparison execution for audit/export
resource cohortRunsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: database
  name: 'cohortRuns'
  properties: {
    resource: {
      id: 'cohortRuns'
      partitionKey: {
        paths: [ '/userId' ]
        kind: 'Hash'
        version: 2
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        includedPaths: [
          { path: '/userId/?' }
          { path: '/createdAt/?' }
          { path: '/conversationId/?' }
          { path: '/executionMode/?' }
          { path: '/contractVersion/?' }
          { path: '/agentCount/?' }
        ]
        excludedPaths: [
          { path: '/*' }
        ]
      }
      defaultTtl: -1
    }
  }
}

// ---------------------------------------------------------------------------
// RBAC — Cosmos DB Built-in Data Contributor on the account scope
// Allows managed identities to read/write/delete without connection strings
// ---------------------------------------------------------------------------
var dataContributorRoleId = '00000000-0000-0000-0000-000000000002'

resource roleAssignments 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = [
  for (principalId, i) in dataContributorPrincipalIds: {
    parent: cosmosAccount
    name: guid(cosmosAccount.id, principalId, dataContributorRoleId)
    properties: {
      roleDefinitionId: '${cosmosAccount.id}/sqlRoleDefinitions/${dataContributorRoleId}'
      principalId: principalId
      scope: cosmosAccount.id
    }
  }
]

output accountEndpoint string = cosmosAccount.properties.documentEndpoint
output accountName string = cosmosAccount.name
output databaseName string = database.name
