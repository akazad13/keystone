#Requires -Version 7
<#
.SYNOPSIS
    Configures the local ZITADEL for Keystone. Safe to run more than once.

.DESCRIPTION
    Uses the "keystone-setup" machine user created on ZITADEL's first start (see docker-compose.yml)
    to create, through the Management API:
      - project "Keystone" with roles Admin and User (roles are asserted into tokens/userinfo)
      - OIDC web app "keystone-bff": authorization code + refresh token, client_secret_post,
        redirect / post-logout / back-channel logout URIs pointing at the BFF
      - two local test users: alice (Admin) and bob (User)
    and stores the app's client id and secret in the AppHost's user secrets.

    Every step is something you can also do by hand in the console at http://localhost:8080/ui/console.
#>
[CmdletBinding()]
param(
    [string] $ZitadelUrl = 'http://localhost:8080',
    [string] $BffUrl = 'https://localhost:7003',
    # ZITADEL runs in Docker; it reaches the BFF on the host through host.docker.internal.
    # Plain http on purpose: the container does not trust the ASP.NET Core development certificate.
    [string] $BackChannelLogoutUri = 'http://host.docker.internal:5003/bff/backchannel-logout'
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# --- Local test users (development only, never used outside this local ZITADEL) ---
$TestUsers = @(
    @{ UserName = 'alice'; FirstName = 'Alice'; LastName = 'Admin'; Role = 'Admin' }
    @{ UserName = 'bob';   FirstName = 'Bob';   LastName = 'User';  Role = 'User' }
)
$TestPassword = 'Keystone-Dev-1!'

# --- Personal access token of the setup machine user ---
docker compose cp zitadel-api:/zitadel/bootstrap/keystone-setup.pat ./.setup.pat | Out-Null
$headers = @{ Authorization = "Bearer $((Get-Content ./.setup.pat -Raw).Trim())" }

function Invoke-Zitadel([string] $Method, [string] $Path, $Body) {
    $json = if ($null -ne $Body) { $Body | ConvertTo-Json -Depth 10 } else { $null }
    Invoke-RestMethod -Method $Method -Uri "$ZitadelUrl$Path" -Headers $headers -ContentType 'application/json' -Body $json
}

# --- Project and roles ---
$project = (Invoke-Zitadel POST '/management/v1/projects/_search' @{
    queries = @(@{ nameQuery = @{ name = 'Keystone'; method = 'TEXT_QUERY_METHOD_EQUALS' } })
}).result | Select-Object -First 1

if (-not $project) {
    $project = Invoke-Zitadel POST '/management/v1/projects' @{
        name = 'Keystone'
        # Put the user's roles in userinfo and tokens without the client asking for them per request.
        projectRoleAssertion = $true
    }
    Invoke-Zitadel POST "/management/v1/projects/$($project.id)/roles/_bulk" @{
        roles = @(
            @{ key = 'Admin'; displayName = 'Admin' }
            @{ key = 'User';  displayName = 'User' }
        )
    } | Out-Null
    Write-Host "Created project Keystone ($($project.id))"
}
$projectId = $project.id

# --- OIDC application for the BFF ---
$app = (Invoke-Zitadel POST "/management/v1/projects/$projectId/apps/_search" @{
    queries = @(@{ nameQuery = @{ name = 'keystone-bff'; method = 'TEXT_QUERY_METHOD_EQUALS' } })
}).result | Select-Object -First 1

if (-not $app) {
    $created = Invoke-Zitadel POST "/management/v1/projects/$projectId/apps/oidc" @{
        name                   = 'keystone-bff'
        appType                = 'OIDC_APP_TYPE_WEB'
        responseTypes          = @('OIDC_RESPONSE_TYPE_CODE')
        grantTypes             = @('OIDC_GRANT_TYPE_AUTHORIZATION_CODE', 'OIDC_GRANT_TYPE_REFRESH_TOKEN')
        # The ASP.NET Core OIDC handler sends client_id/client_secret in the token request body.
        authMethodType         = 'OIDC_AUTH_METHOD_TYPE_POST'
        redirectUris           = @("$BffUrl/signin-oidc")
        postLogoutRedirectUris = @("$BffUrl/signout-callback-oidc")
        backChannelLogoutUri   = $BackChannelLogoutUri
        accessTokenType        = 'OIDC_TOKEN_TYPE_BEARER'
        # Development mode allows http URIs (the back-channel logout URI above).
        devMode                = $true
    }
    $clientId = $created.clientId
    $clientSecret = $created.clientSecret
    Write-Host "Created OIDC app keystone-bff ($clientId)"
}
else {
    # The secret is only shown once, so issue a new one for an existing app.
    $clientId = $app.oidcConfig.clientId
    $clientSecret = (Invoke-Zitadel POST "/management/v1/projects/$projectId/apps/$($app.id)/oidc_config/_generate_client_secret" @{}).clientSecret
    Write-Host "Generated a new client secret for keystone-bff ($clientId)"
}

# --- Test users and their role grants ---
foreach ($user in $TestUsers) {
    $existing = (Invoke-Zitadel POST '/management/v1/users/_search' @{
        queries = @(@{ userNameQuery = @{ userName = $user.UserName; method = 'TEXT_QUERY_METHOD_EQUALS' } })
    }).result | Select-Object -First 1

    if ($existing) { continue }

    $created = Invoke-Zitadel POST '/management/v1/users/human/_import' @{
        userName               = $user.UserName
        profile                = @{ firstName = $user.FirstName; lastName = $user.LastName; displayName = "$($user.FirstName) $($user.LastName)" }
        email                  = @{ email = "$($user.UserName)@keystone.localhost"; isEmailVerified = $true }
        password               = $TestPassword
        passwordChangeRequired = $false
    }
    Invoke-Zitadel POST "/management/v1/users/$($created.userId)/grants" @{
        projectId = $projectId
        roleKeys  = @($user.Role)
    } | Out-Null
    Write-Host "Created user $($user.UserName) with role $($user.Role)"
}

# --- Hand the client credentials to the AppHost (user secrets, never committed) ---
$appHost = Join-Path $PSScriptRoot '..\Keystone.AppHost'
dotnet user-secrets set 'Parameters:oidc-client-id' $clientId --project $appHost | Out-Null
dotnet user-secrets set 'Parameters:oidc-client-secret' $clientSecret --project $appHost | Out-Null

Write-Host ''
Write-Host 'ZITADEL is ready for Keystone.'
Write-Host "  Client id stored in Keystone.AppHost user secrets: $clientId"
Write-Host "  Test users: alice (Admin), bob (User); password is in this script (`$TestPassword)."
Write-Host "  Run the app:  dotnet run --project Keystone.AppHost, then open $BffUrl"
