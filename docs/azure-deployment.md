# Deploying Keystone to Azure

`aspire deploy` turns the AppHost into Azure resources: it generates Bicep, provisions the infrastructure, builds the container images, pushes them to a registry and creates the Container Apps. This guide takes you from nothing to a working, signed-in Keystone in Azure.

## What gets created

| Resource | Notes |
|----------|-------|
| Resource group | You choose the name when prompted |
| Container Apps environment `keystone-env` | With a Log Analytics workspace and the Aspire dashboard |
| Azure Container Registry + user-assigned managed identity | The apps pull images with the identity, no registry passwords |
| Container App `bff` | **External** ingress (HTTPS). Serves both SPAs from `wwwroot`. |
| Container Apps `identity-service`, `core-api` | Internal ingress only, unreachable from the internet |
| Azure SQL server + database `keystonedb` | Entra ID authentication only. A deployment script grants the Core API's managed identity access. |
| Azure Managed Redis | Entra ID authentication. The BFF and Core API get data access through their managed identities. |

The identity provider is **not** deployed. In Azure, Keystone uses a hosted Zitadel instance (Zitadel Cloud). See [ARCHITECTURE.md, decision D5](../ARCHITECTURE.md#7-key-decisions).

> **Cost.** Azure SQL Database and Azure Managed Redis are billed while they exist, even when idle. Run `aspire destroy` when you are done experimenting (see [Tear down](#tear-down)).

## Prerequisites

* An Azure subscription where you can create resources **and role assignments** (Owner, or Contributor plus User Access Administrator).
* [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli), signed in: `az login`.
* The Aspire CLI: `irm https://aspire.dev/install.ps1 | iex` (Windows) or `curl -sSL https://aspire.dev/install.sh | bash`.
* Docker running (the images are built on your machine).
* A Zitadel Cloud account (free tier): <https://zitadel.com>.

## Step 1: Prepare Zitadel Cloud

In your Zitadel Cloud instance's console, repeat what `zitadel/setup.ps1` does locally:

1. **Note the instance URL**, for example `https://keystone-abc123.zitadel.cloud`. This is the `oidc-authority` parameter.
2. **Create a project** named `Keystone`. In its settings, enable **Assert Roles on Authentication**.
3. **Add roles** `Admin` and `User` to the project.
4. **Create an application** in the project:
   * Type **Web**, authentication method **POST** (`client_secret_post`).
   * Grant types: **Authorization Code** and **Refresh Token**.
   * Redirect URI: `https://placeholder.invalid/signin-oidc` for now. You will replace it after the first deploy, once the BFF's address is known.
   * Copy the **client id** and **client secret** when they are shown. The secret is only displayed once.
5. **Create a user** for yourself (or use your admin user) and **authorize** it for the Keystone project with role `Admin` (Users > your user > Authorizations).

## Step 2: Create the token signing key

The Identity Service signs Keystone tokens with an RSA key. Every replica must use the same key, so you supply it as a secret parameter. In PowerShell 7:

```powershell
$rsa = [System.Security.Cryptography.RSA]::Create(2048)
[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($rsa.ExportPkcs8PrivateKeyPem())) | Set-Clipboard
```

The base64-encoded PEM is now on your clipboard, ready to paste at the deploy prompt. Do not save it in a file in the repository.

## Step 3: Deploy

From the repository root:

```powershell
az login
aspire deploy
```

The CLI finds `Keystone.AppHost` (choose it if asked), then prompts for:

| Prompt | Value |
|--------|-------|
| Subscription, location, resource group | Your choice |
| `oidc-authority` | Your Zitadel Cloud instance URL |
| `oidc-client-id` | From step 1 |
| `oidc-client-secret` | From step 1 |
| `identity-signing-key` | From step 2 |

Answers are saved for later deploys. Secret parameters end up as **Container App secrets**, not as plain environment variables.

The first deployment takes a while (SQL and Redis provisioning is slow). The output ends with the endpoints. Note the BFF's URL, for example `https://bff.purplesky-1234abcd.westeurope.azurecontainerapps.io`.

Run `aspire deploy --list-steps` to see what it will do without doing it. The generated Bicep is worth reading: `aspire publish` writes it to disk.

## Step 4: Point Zitadel at the deployed BFF

Back in the Zitadel console, edit the application's URLs. Replace `<bff>` with the BFF's host name:

| Setting | Value |
|---------|-------|
| Redirect URI | `https://<bff>/signin-oidc` |
| Post-logout redirect URI | `https://<bff>/signout-callback-oidc` |
| Back-channel logout URI | `https://<bff>/bff/backchannel-logout` |

No redeploy is needed: these settings live in Zitadel.

## Step 5: Try it

1. Open `https://<bff>/`: the portal. `https://<bff>/admin/` is the admin dashboard.
2. Sign in with your Zitadel user. You land back on the portal, signed in.
3. Open the dashboard page. The API call travels BFF > Identity Service (token exchange, which calls Zitadel Cloud's userinfo) > Core API > Azure SQL.
4. Test back-channel logout: sign in, then sign out of Zitadel Cloud's own console (or end the session there). Refocus the Keystone tab: you are signed out.

## Observability

* **Aspire dashboard in Azure:** in the Azure portal, open the Container Apps environment `keystone-env` and find **Aspire Dashboard**. It shows the same structured logs, traces and metrics as locally.
* **Log Analytics:** container console logs land in the environment's workspace (`ContainerAppConsoleLogs_CL`).
* **Container App logs:** `az containerapp logs show -n bff -g <resource-group> --follow`.

## Day two

| Task | Command |
|------|---------|
| Deploy a new version | `aspire deploy` (reuses saved answers) |
| Change a parameter (e.g. a rotated client secret) | `aspire deploy --clear-cache`, then answer the prompts again |
| See what changed | `aspire publish` and diff the generated Bicep |
| Remove everything | `aspire destroy` (asks before deleting the resource group) |

### How the Azure-specific bits work

* **Managed identities instead of passwords.** `AddAzureSqlServer` and `AddAzureManagedRedis` configure Entra ID authentication only. The services use `AddSqlServerDbContext` and `AddRedisClientBuilder(...).WithAzureAuthentication()`, which pick up the Container App's managed identity. The same code uses passwords against the local containers.
* **TLS termination.** Container Apps terminates HTTPS at its ingress and forwards plain HTTP. Aspire sets `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, so the BFF sees `https` and builds correct redirect URIs and Secure cookies.
* **Frontends inside the BFF.** `bff.PublishWithContainerFiles(keystone, "./wwwroot")` builds each SPA in a Node container (`npm run build`) and copies `dist` into the BFF image. In production the BFF serves them with `UseStaticFiles` and falls back to the right `index.html` for client-side routes.
* **One signing key for all replicas.** The `identity-signing-key` parameter becomes `Jwt__SigningKey`. The key id is the key's RFC 7638 thumbprint, so every replica publishes identical JWKS.

### Tear down

```powershell
aspire destroy
```

This deletes the whole resource group, including anything you added to it by hand.

## Hardening checklist (beyond this learning project)

| Item | Why |
|------|-----|
| Custom domain for the BFF (`ConfigureCustomDomain` in `PublishAsAzureContainerApp`) | Stable redirect URIs, your own certificate |
| Signing key in Key Vault, with rotation (publish the old and new key in JWKS during the overlap) | Key never handled by people; compromise recovery |
| Health probes on `/health` and `/alive` (currently mapped only in Development) | Container Apps can restart unhealthy replicas |
| Authenticate calls to the Identity Service's `/connect/token` (client credentials or mTLS) | Defence in depth beyond network isolation |
| Minimum replicas and scale rules per app (`PublishAsAzureContainerApp`) | Availability and cost control |
| CI/CD pipeline running `aspire deploy` non-interactively with a federated identity | Repeatable deployments without personal credentials |
| Application Insights (`AddAzureApplicationInsights`) | Long-term telemetry retention and alerts |
