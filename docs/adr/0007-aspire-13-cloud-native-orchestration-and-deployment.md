# 0007. Cloud-native orchestration and deployment with .NET Aspire 13

- **Status:** Accepted
- **Date:** 2026-10-17
- **Architecture Ref:** D6 ([ARCHITECTURE.md](../../ARCHITECTURE.md#7-key-decisions))

## Context

Managing multi-service enterprise solutions locally requires coordinated process launch, service discovery, distributed configuration injection, and telemetry collection across frontends and backends. In cloud environments, provisioning Azure Container Apps, Azure SQL, Azure Managed Redis, and container registries typically involves managing hundreds of lines of separate Terraform or Bicep templates that easily drift from application configuration.

## Decision

Standardize on **.NET Aspire 13.6** with `aspire deploy` across the entire solution:
- `Keystone.AppHost` serves as the single source of truth for resources, dependencies, environment parameters, and container mappings.
- `Keystone.ServiceDefaults` wires OpenTelemetry metrics, distributed traces, health checks (`/health`, `/alive`), and HTTP resilience into every service.
- Azure deployment generates Bicep templates on the fly, builds container images, and deploys directly to Azure Container Apps.
- Relies on managed identities for passwordless connection to Azure SQL Database and Azure Managed Redis.

### Rejected Alternative

Aspire 9 or manual Docker Compose and hand-maintained Terraform/Bicep configurations. Aspire 9 is out of support. Manual infrastructure templates introduce configuration drift and require handling database and cache passwords rather than native Azure managed identities.

## Consequences

- A single command (`dotnet run --project Keystone.AppHost`) starts all backend services, proxies, databases, and frontends locally with an interactive Aspire dashboard.
- Azure deployment is fully automated via `aspire deploy`, mapping services to Azure Container Apps and Log Analytics.
- Passwordless connectivity using Microsoft Entra managed identities eliminates database and cache connection string secrets from production configuration.
