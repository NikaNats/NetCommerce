# NetCommerce Deployment Guide

> **Production deployment, infrastructure, and environment configuration**

---

## Table of Contents

1. [Deployment Overview](#deployment-overview)
2. [Local Development](#local-development)
3. [Environment Configuration](#environment-configuration)
4. [Infrastructure Requirements](#infrastructure-requirements)
5. [Container Orchestration](#container-orchestration)
6. [Database Migrations](#database-migrations)
7. [Keycloak Setup](#keycloak-setup)
8. [Production Checklist](#production-checklist)
9. [Scaling Considerations](#scaling-considerations)
10. [Disaster Recovery](#disaster-recovery)

---

## Deployment Overview

### Aspire Orchestration

NetCommerce uses **.NET Aspire** for both local development and production deployment orchestration:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    DEPLOYMENT ARCHITECTURE                                   │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  LOCAL DEVELOPMENT (Aspire AppHost)                                         │
│  ┌──────────────────────────────────────────────────────────────────┐      │
│  │  dotnet run --project src/NetCommerce.AppHost                     │      │
│  │                                                                   │      │
│  │  Starts automatically:                                            │      │
│  │  • PostgreSQL (5 databases)                                       │      │
│  │  • Redis                                                          │      │
│  │  • Keycloak 26                                                    │      │
│  │  • Seq (logging)                                                  │      │
│  │  • Meilisearch                                                    │      │
│  │  • Azurite (blob storage emulator)                               │      │
│  │  • NetCommerce API                                                │      │
│  └──────────────────────────────────────────────────────────────────┘      │
│                                                                             │
│  PRODUCTION (Azure Container Apps / Kubernetes)                             │
│  ┌──────────────────────────────────────────────────────────────────┐      │
│  │  aspire publish                                                   │      │
│  │                                                                   │      │
│  │  Generates:                                                       │      │
│  │  • Container images                                               │      │
│  │  • Kubernetes manifests                                           │      │
│  │  • Azure Bicep/ARM templates                                      │      │
│  │  • Environment variable configurations                            │      │
│  └──────────────────────────────────────────────────────────────────┘      │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Deployment Models

| Model | Use Case | Infrastructure |
|-------|----------|----------------|
| Local | Development | Docker Desktop + Aspire |
| Azure Container Apps | Small/Medium | Managed containers |
| Azure Kubernetes Service | Large | Full K8s control |
| On-Premises | Enterprise | Self-managed K8s |

---

## Local Development

### Prerequisites

```powershell
# 1. Install .NET 10 SDK
winget install Microsoft.DotNet.SDK.Preview

# 2. Install Docker Desktop
winget install Docker.DockerDesktop

# 3. Install Aspire workload
dotnet workload install aspire

# 4. Verify installation
dotnet workload list
# Should show: aspire
```

### Start the Application

```powershell
# Clone repository
git clone https://github.com/your-org/NetCommerce.git
cd NetCommerce

# Run with Aspire (starts all infrastructure)
dotnet run --project src/NetCommerce.AppHost/NetCommerce.AppHost.csproj
```

### Aspire Dashboard

After starting, the Aspire dashboard is available at:

- **Dashboard**: https://localhost:17235
- **API**: https://localhost:7001
- **Swagger**: https://localhost:7001/swagger
- **Keycloak**: http://localhost:8080 (admin/admin)
- **pgAdmin**: http://localhost:5050
- **Redis Insight**: http://localhost:8001
- **Seq**: http://localhost:5341

### Data Persistence

Aspire containers use persistent volumes:

```csharp
// src/NetCommerce.AppHost/Program.cs
var postgres = builder.AddPostgres("postgres", password: postgresPassword)
    .WithDataVolume()                         // Persistent storage
    .WithLifetime(ContainerLifetime.Persistent);  // Survive restarts

var redis = builder.AddRedis("redis")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);
```

### Reset Local Environment

```powershell
# Stop all containers
docker compose down -v

# Remove Aspire volumes (fresh start)
docker volume prune -f

# Restart
dotnet run --project src/NetCommerce.AppHost/NetCommerce.AppHost.csproj
```

---

## Environment Configuration

### Configuration Hierarchy

```
1. appsettings.json           (base configuration)
2. appsettings.{Environment}.json  (environment-specific)
3. Environment variables       (overrides, secrets)
4. Aspire configuration        (injected automatically)
```

### Key Environment Variables

```bash
# ─────────────────────────────────────────────────────────────────────────────
# DATABASE CONNECTIONS (Injected by Aspire)
# ─────────────────────────────────────────────────────────────────────────────
ConnectionStrings__CatalogDb=Host=postgres;Database=catalog;Username=postgres;Password=...
ConnectionStrings__OrderingDb=Host=postgres;Database=ordering;Username=postgres;Password=...
ConnectionStrings__InventoryDb=Host=postgres;Database=inventory;Username=postgres;Password=...
ConnectionStrings__PaymentsDb=Host=postgres;Database=payments;Username=postgres;Password=...

# ─────────────────────────────────────────────────────────────────────────────
# REDIS (Injected by Aspire)
# ─────────────────────────────────────────────────────────────────────────────
ConnectionStrings__redis=redis:6379

# ─────────────────────────────────────────────────────────────────────────────
# KEYCLOAK IDENTITY
# ─────────────────────────────────────────────────────────────────────────────
Keycloak__AuthServerUrl=http://keycloak:8080
Keycloak__Realm=netcommerce

# Override for production
Auth__Audience=netcommerce-api
Auth__ApiScope=netcommerce.api
Auth__ClientId=netcommerce-api
Auth__ClientSecret=<from-key-vault>    # NEVER hardcode in production!
Auth__IntrospectionEnabled=true
Auth__IntrospectionCacheSeconds=30
Auth__TokenExchangeEnabled=true

# ─────────────────────────────────────────────────────────────────────────────
# MEILISEARCH
# ─────────────────────────────────────────────────────────────────────────────
ConnectionStrings__meilisearch=http://meilisearch:7700
Meilisearch__MasterKey=<from-key-vault>

# ─────────────────────────────────────────────────────────────────────────────
# AZURE BLOB STORAGE
# ─────────────────────────────────────────────────────────────────────────────
# Development (Azurite)
ConnectionStrings__blobs=AccountName=devstoreaccount1;AccountKey=...;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1

# Production
ConnectionStrings__blobs=DefaultEndpointsProtocol=https;AccountName=netcommerceblobs;AccountKey=...

# ─────────────────────────────────────────────────────────────────────────────
# SEQ LOGGING
# ─────────────────────────────────────────────────────────────────────────────
# Development
Seq__ServerUrl=http://seq:5341

# Production (use Azure Monitor or Datadog instead)
Logging__LogLevel__Default=Warning
```

### appsettings.json Structure

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Wolverine": "Information"
    }
  },
  "Auth": {
    "Audience": "netcommerce-api",
    "ApiScope": "netcommerce.api",
    "IntrospectionEnabled": true,
    "IntrospectionCacheSeconds": 30,
    "TokenExchangeEnabled": true
  },
  "Features": {
    "EnableMeilisearch": true,
    "EnableBlobStorage": true,
    "MaxRequestBodySizeBytes": 10485760
  }
}
```

### appsettings.Production.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Microsoft.AspNetCore": "Warning",
      "Wolverine": "Warning"
    }
  },
  "Auth": {
    "IntrospectionEnabled": true,
    "IntrospectionCacheSeconds": 30
  }
}
```

---

## Infrastructure Requirements

### Production Infrastructure

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    PRODUCTION INFRASTRUCTURE                                 │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  COMPUTE                                                                    │
│  ┌──────────────────────────────────────────────────────────────────┐      │
│  │  NetCommerce API                                                  │      │
│  │  • Min: 2 replicas (HA)                                          │      │
│  │  • CPU: 1 vCPU per replica                                       │      │
│  │  • Memory: 2GB per replica                                       │      │
│  │  • Auto-scale: 2-10 replicas (CPU > 70%)                        │      │
│  └──────────────────────────────────────────────────────────────────┘      │
│                                                                             │
│  DATABASES                                                                   │
│  ┌──────────────────────────────────────────────────────────────────┐      │
│  │  PostgreSQL (Azure Database for PostgreSQL - Flexible Server)    │      │
│  │  • SKU: General Purpose (4 vCores, 16GB) minimum                │      │
│  │  • Storage: 256GB with auto-grow                                │      │
│  │  • High Availability: Zone-redundant                            │      │
│  │  • Backup: Geo-redundant, 35-day retention                      │      │
│  │                                                                   │      │
│  │  Databases:                                                       │      │
│  │  • catalog (Catalog module)                                      │      │
│  │  • ordering (Ordering module + Wolverine)                        │      │
│  │  • inventory (Inventory module)                                  │      │
│  │  • payments (Payments module)                                    │      │
│  │  • keycloak (Identity)                                           │      │
│  └──────────────────────────────────────────────────────────────────┘      │
│                                                                             │
│  CACHING                                                                     │
│  ┌──────────────────────────────────────────────────────────────────┐      │
│  │  Azure Cache for Redis                                            │      │
│  │  • SKU: Standard C1 (1GB) minimum                                │      │
│  │  • Clustering: Enabled for > 1GB                                 │      │
│  │  • Used for: Session, introspection cache, basket, distributed   │      │
│  │    locking                                                        │      │
│  └──────────────────────────────────────────────────────────────────┘      │
│                                                                             │
│  IDENTITY                                                                    │
│  ┌──────────────────────────────────────────────────────────────────┐      │
│  │  Keycloak (Azure Container Apps or AKS)                          │      │
│  │  • 2 replicas (HA)                                               │      │
│  │  • PostgreSQL backend                                            │      │
│  │  • Redis for session clustering                                  │      │
│  └──────────────────────────────────────────────────────────────────┘      │
│                                                                             │
│  STORAGE                                                                     │
│  ┌──────────────────────────────────────────────────────────────────┐      │
│  │  Azure Blob Storage                                               │      │
│  │  • Tier: Hot (frequently accessed product images)                │      │
│  │  • Redundancy: ZRS or GRS                                        │      │
│  │  • CDN: Azure CDN for public images                              │      │
│  └──────────────────────────────────────────────────────────────────┘      │
│                                                                             │
│  SEARCH                                                                      │
│  ┌──────────────────────────────────────────────────────────────────┐      │
│  │  Meilisearch (Azure Container Apps)                              │      │
│  │  • 1 replica (with persistent volume)                            │      │
│  │  • Memory: 2GB minimum                                           │      │
│  │  • Storage: SSD for index                                        │      │
│  └──────────────────────────────────────────────────────────────────┘      │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Resource Sizing Guide

| Component | Development | Staging | Production |
|-----------|-------------|---------|------------|
| API Replicas | 1 | 2 | 2-10 (auto) |
| API Memory | 512MB | 1GB | 2GB |
| PostgreSQL vCores | 2 | 4 | 8+ |
| PostgreSQL Storage | 32GB | 128GB | 256GB+ |
| Redis Memory | 250MB | 1GB | 6GB |
| Meilisearch Memory | 512MB | 1GB | 2GB |

---

## Container Orchestration

### Docker Build

```dockerfile
# Dockerfile (multi-stage build)
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Copy solution and project files
COPY NetCommerce.slnx .
COPY Directory.Build.props .
COPY Directory.Packages.props .
COPY src/ src/

# Restore
RUN dotnet restore NetCommerce.slnx

# Build
RUN dotnet build src/Api/NetCommerce.Api.csproj -c Release --no-restore

# Publish
RUN dotnet publish src/Api/NetCommerce.Api.csproj -c Release -o /app --no-build

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime
WORKDIR /app

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:8080/health/ready || exit 1

COPY --from=build /app .
ENTRYPOINT ["dotnet", "NetCommerce.Api.dll"]
```

### Kubernetes Deployment

```yaml
# k8s/deployment.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: netcommerce-api
  labels:
    app: netcommerce-api
spec:
  replicas: 2
  selector:
    matchLabels:
      app: netcommerce-api
  template:
    metadata:
      labels:
        app: netcommerce-api
    spec:
      containers:
      - name: api
        image: netcommerce.azurecr.io/netcommerce-api:latest
        ports:
        - containerPort: 8080
        resources:
          requests:
            memory: "1Gi"
            cpu: "500m"
          limits:
            memory: "2Gi"
            cpu: "1000m"
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: "Production"
        - name: Auth__ClientSecret
          valueFrom:
            secretKeyRef:
              name: netcommerce-secrets
              key: keycloak-client-secret
        livenessProbe:
          httpGet:
            path: /health/live
            port: 8080
          initialDelaySeconds: 10
          periodSeconds: 10
        readinessProbe:
          httpGet:
            path: /health/ready
            port: 8080
          initialDelaySeconds: 5
          periodSeconds: 5
---
apiVersion: v1
kind: Service
metadata:
  name: netcommerce-api
spec:
  selector:
    app: netcommerce-api
  ports:
  - port: 80
    targetPort: 8080
  type: ClusterIP
---
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: netcommerce-api
  annotations:
    kubernetes.io/ingress.class: nginx
    cert-manager.io/cluster-issuer: letsencrypt-prod
spec:
  tls:
  - hosts:
    - api.netcommerce.com
    secretName: netcommerce-tls
  rules:
  - host: api.netcommerce.com
    http:
      paths:
      - path: /
        pathType: Prefix
        backend:
          service:
            name: netcommerce-api
            port:
              number: 80
```

### Azure Container Apps (Bicep)

```bicep
// infra/main.bicep
param location string = resourceGroup().location
param environmentName string = 'netcommerce'

// Container Apps Environment
resource containerAppEnv 'Microsoft.App/managedEnvironments@2023-05-01' = {
  name: '${environmentName}-env'
  location: location
  properties: {
    daprAIConnectionString: applicationInsights.properties.ConnectionString
  }
}

// NetCommerce API
resource apiApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: '${environmentName}-api'
  location: location
  properties: {
    managedEnvironmentId: containerAppEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
      }
      secrets: [
        {
          name: 'keycloak-client-secret'
          value: keycloakClientSecret
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: '${containerRegistry.properties.loginServer}/netcommerce-api:latest'
          resources: {
            cpu: json('1.0')
            memory: '2Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'Auth__ClientSecret'
              secretRef: 'keycloak-client-secret'
            }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health/live'
                port: 8080
              }
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health/ready'
                port: 8080
              }
            }
          ]
        }
      ]
      scale: {
        minReplicas: 2
        maxReplicas: 10
        rules: [
          {
            name: 'http-scale'
            http: {
              metadata: {
                concurrentRequests: '100'
              }
            }
          }
        ]
      }
    }
  }
}
```

---

## Database Migrations

### EF Core Migrations

Each module maintains its own migrations:

```powershell
# Generate migration for Catalog module
dotnet ef migrations add InitialCreate `
  --project src/Catalog/Catalog.Infrastructure `
  --startup-project src/Api `
  --context CatalogDbContext

# Generate migration for Ordering module
dotnet ef migrations add InitialCreate `
  --project src/Ordering/Ordering.Infrastructure `
  --startup-project src/Api `
  --context OrderingDbContext

# Apply all migrations
dotnet ef database update --project src/Api
```

### Production Migration Strategy

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    MIGRATION STRATEGY                                        │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  APPROACH: Blue-Green with Rolling Migrations                               │
│                                                                             │
│  1. Deploy migration job (separate from API)                                │
│     ┌─────────────────────────────────────────────────────────────┐        │
│     │  Job: netcommerce-migrations                                 │        │
│     │  • Runs EF migrations for all modules                       │        │
│     │  • Waits for database connectivity                          │        │
│     │  • Applies migrations in order                              │        │
│     │  • Exits on success                                         │        │
│     └─────────────────────────────────────────────────────────────┘        │
│                                                                             │
│  2. Wait for migration completion                                           │
│     ┌─────────────────────────────────────────────────────────────┐        │
│     │  CI/CD waits for job success before proceeding              │        │
│     └─────────────────────────────────────────────────────────────┘        │
│                                                                             │
│  3. Rolling deployment of API                                               │
│     ┌─────────────────────────────────────────────────────────────┐        │
│     │  • New pods start with new code                             │        │
│     │  • Old pods drain connections                               │        │
│     │  • Zero downtime                                            │        │
│     └─────────────────────────────────────────────────────────────┘        │
│                                                                             │
│  CRITICAL: Migrations must be backward-compatible!                          │
│  • Add columns as nullable first                                            │
│  • Rename via copy, not ALTER                                              │
│  • Deploy data migration separately from schema change                     │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Migration Job (Kubernetes)

```yaml
# k8s/migration-job.yaml
apiVersion: batch/v1
kind: Job
metadata:
  name: netcommerce-migrations
spec:
  template:
    spec:
      containers:
      - name: migrations
        image: netcommerce.azurecr.io/netcommerce-api:latest
        command: ["dotnet", "NetCommerce.Api.dll", "--migrate"]
        env:
        - name: ConnectionStrings__CatalogDb
          valueFrom:
            secretKeyRef:
              name: db-secrets
              key: catalog-connection
      restartPolicy: Never
  backoffLimit: 3
```

---

## Keycloak Setup

### Production Keycloak Configuration

```yaml
# k8s/keycloak-deployment.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: keycloak
spec:
  replicas: 2
  template:
    spec:
      containers:
      - name: keycloak
        image: quay.io/keycloak/keycloak:26.0
        args: ["start"]
        env:
        - name: KC_DB
          value: "postgres"
        - name: KC_DB_URL
          value: "jdbc:postgresql://postgres:5432/keycloak"
        - name: KC_DB_USERNAME
          valueFrom:
            secretKeyRef:
              name: keycloak-db
              key: username
        - name: KC_DB_PASSWORD
          valueFrom:
            secretKeyRef:
              name: keycloak-db
              key: password
        - name: KC_HOSTNAME
          value: "auth.netcommerce.com"
        - name: KC_PROXY
          value: "edge"  # Behind load balancer
        - name: KC_FEATURES
          value: "token-exchange,admin-fine-grained-authz"
        - name: KC_CACHE
          value: "ispn"
        - name: KC_CACHE_STACK
          value: "kubernetes"
        - name: KC_HEALTH_ENABLED
          value: "true"
        - name: KC_METRICS_ENABLED
          value: "true"
```

### Realm Import

```json
{
  "realm": "netcommerce",
  "enabled": true,
  "sslRequired": "external",
  "roles": {
    "realm": [
      { "name": "admin", "description": "Administrator" },
      { "name": "vendor", "description": "Vendor/Seller" },
      { "name": "customer", "description": "Customer" }
    ]
  },
  "clients": [
    {
      "clientId": "netcommerce-api",
      "enabled": true,
      "clientAuthenticatorType": "client-secret",
      "secret": "${KEYCLOAK_CLIENT_SECRET}",
      "serviceAccountsEnabled": true,
      "authorizationServicesEnabled": true,
      "directAccessGrantsEnabled": false,
      "standardFlowEnabled": false,
      "protocolMappers": [
        {
          "name": "audience",
          "protocol": "openid-connect",
          "protocolMapper": "oidc-audience-mapper",
          "config": {
            "included.client.audience": "netcommerce-api",
            "access.token.claim": "true"
          }
        }
      ]
    },
    {
      "clientId": "netcommerce-frontend",
      "enabled": true,
      "publicClient": true,
      "standardFlowEnabled": true,
      "directAccessGrantsEnabled": false,
      "webOrigins": ["https://netcommerce.com", "http://localhost:3000"],
      "redirectUris": ["https://netcommerce.com/*", "http://localhost:3000/*"],
      "attributes": {
        "pkce.code.challenge.method": "S256"
      }
    }
  ]
}
```

---

## Production Checklist

### Pre-Deployment

- [ ] **Security**
  - [ ] Auth__ClientSecret in Key Vault (not env var)
  - [ ] Meilisearch master key in Key Vault
  - [ ] PostgreSQL passwords in Key Vault
  - [ ] HTTPS certificates issued
  - [ ] Token introspection enabled (`Auth__IntrospectionEnabled=true`)
  - [ ] Network policies configured (pod-to-pod isolation)

- [ ] **Database**
  - [ ] Migrations tested in staging
  - [ ] Backup configured and tested
  - [ ] Connection pooling configured (PgBouncer or built-in)
  - [ ] Read replicas for reporting (optional)

- [ ] **Observability**
  - [ ] Application Insights / Datadog configured
  - [ ] Alerts for error rate > 1%
  - [ ] Alerts for P99 latency > 500ms
  - [ ] Dashboard for key metrics

- [ ] **Performance**
  - [ ] Load testing completed
  - [ ] Auto-scaling configured and tested
  - [ ] CDN configured for static assets
  - [ ] Redis connection pooling

### Post-Deployment

- [ ] Health checks passing (`/health/ready`)
- [ ] Logs flowing to observability platform
- [ ] Smoke tests passing
- [ ] Rollback plan tested

---

## Scaling Considerations

### Horizontal Scaling

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    SCALING ARCHITECTURE                                      │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  STATELESS COMPONENTS (Scale Out)                                           │
│  ├── NetCommerce API: 2-10 replicas                                        │
│  ├── Keycloak: 2-4 replicas (with infinispan clustering)                  │
│  └── Meilisearch: 1 replica (search is single-writer)                     │
│                                                                             │
│  STATEFUL COMPONENTS (Scale Up, then Shard)                                 │
│  ├── PostgreSQL: Vertical first, then read replicas, then Citus           │
│  └── Redis: Vertical first, then cluster mode                              │
│                                                                             │
│  BOTTLENECK ANALYSIS:                                                       │
│                                                                             │
│  1. Database connections                                                     │
│     └── Solution: PgBouncer connection pooling (100 pool → 1000 clients)  │
│                                                                             │
│  2. Wolverine outbox processing                                             │
│     └── Solution: Increase batch size, add more workers                    │
│                                                                             │
│  3. Token introspection                                                      │
│     └── Solution: Already cached (30s TTL), increase cache if needed       │
│                                                                             │
│  4. Meilisearch indexing                                                     │
│     └── Solution: Batch updates, async indexing                            │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Connection Pooling

```csharp
// Configure EF Core connection pooling
builder.Services.AddDbContext<OrderingDbContext>(options =>
{
    options.UseNpgsql(connectionString, npgsql =>
    {
        npgsql.EnableRetryOnFailure(3);
        npgsql.CommandTimeout(30);
    });
},
poolSize: 100);  // Connection pool size
```

---

## Disaster Recovery

### Backup Strategy

| Component | Backup Frequency | Retention | Recovery Time |
|-----------|------------------|-----------|---------------|
| PostgreSQL | Continuous (PITR) | 35 days | < 1 hour |
| Redis | Hourly snapshot | 7 days | < 15 minutes |
| Blob Storage | Geo-redundant | Indefinite | < 30 minutes |
| Keycloak Config | Daily export | 30 days | < 1 hour |

### Recovery Procedure

```
SCENARIO: Complete region failure

1. DNS FAILOVER (Automatic)
   └── Azure Traffic Manager routes to DR region

2. DATABASE FAILOVER (< 1 hour)
   └── Promote read replica to primary
   └── Update connection strings via Key Vault

3. REDIS RECOVERY (< 15 minutes)
   └── Deploy new Redis from backup
   └── Clear introspection cache (forces re-validation)

4. VERIFICATION
   └── Health checks passing
   └── Smoke tests passing
   └── Monitor error rates for 30 minutes
```

### RTO/RPO Targets

| Metric | Target | Achieved |
|--------|--------|----------|
| **RTO** (Recovery Time) | < 4 hours | 1-2 hours |
| **RPO** (Data Loss) | < 15 minutes | 5 minutes (PITR) |

---

**Document Version:** 1.0
**Last Updated:** February 2026
**Maintainer:** NetCommerce Platform Team
