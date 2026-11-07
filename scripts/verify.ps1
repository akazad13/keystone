# Fast local verification script for AI agents and developers.
# Verifies all quality constraints across .NET services and React frontends.
# Exits with non-zero status on any failure or constraint violation.

$ErrorActionPreference = "Stop"

Write-Host "==> 1. Checking Quality Floor (banned packages & suppression comments)..." -ForegroundColor Cyan

# Check for banned packages in project files
$bannedMatches = Select-String -Path "*.csproj", "tests/**/*.csproj", "Directory.Packages.props" -Pattern "Include\s*=\s*`"(Moq|AutoMapper|NSubstitute)`""
if ($bannedMatches) {
    Write-Error "Constraint violation: Banned packages detected:`n$bannedMatches"
    exit 1
}

# Check for skipped tests or test suppressions
$skippedTests = Select-String -Path "tests/**/*.cs", "keystone/tests/**/*.{ts,tsx}", "keystone-admin/tests/**/*.{ts,tsx}" -Pattern "Fact\s*\(\s*Skip|it\.skip|test\.skip|describe\.skip"
if ($skippedTests) {
    Write-Error "Constraint violation: Skipped tests detected:`n$skippedTests"
    exit 1
}

# Check for ts-ignore or ts-nocheck
$tsSuppressions = Select-String -Path "keystone/src/**/*.{ts,tsx}", "keystone-admin/src/**/*.{ts,tsx}" -Pattern "@ts-ignore|@ts-nocheck"
if ($tsSuppressions) {
    Write-Error "Constraint violation: TypeScript suppression comments detected:`n$tsSuppressions"
    exit 1
}

Write-Host "Quality floor passed." -ForegroundColor Green

Write-Host "`n==> 2. Building .NET Solution in Release mode (warnings treated as errors)..." -ForegroundColor Cyan
dotnet build Keystone.sln --configuration Release /warnaserror
if ($LASTEXITCODE -ne 0) {
    Write-Error ".NET build failed."
    exit $LASTEXITCODE
}

Write-Host "`n==> 3. Running .NET unit & integration tests (222 tests via MTP)..." -ForegroundColor Cyan
dotnet test Keystone.sln --no-build --configuration Release
if ($LASTEXITCODE -ne 0) {
    Write-Error ".NET tests failed."
    exit $LASTEXITCODE
}

Write-Host "`n==> 4. Type-checking and building frontend applications..." -ForegroundColor Cyan
npm run build --prefix keystone
if ($LASTEXITCODE -ne 0) {
    Write-Error "keystone build failed."
    exit $LASTEXITCODE
}

npm run build --prefix keystone-admin
if ($LASTEXITCODE -ne 0) {
    Write-Error "keystone-admin build failed."
    exit $LASTEXITCODE
}

Write-Host "`n==> 5. Running frontend test suites (Vitest)..." -ForegroundColor Cyan
npm test --prefix keystone
if ($LASTEXITCODE -ne 0) {
    Write-Error "keystone tests failed."
    exit $LASTEXITCODE
}

npm test --prefix keystone-admin
if ($LASTEXITCODE -ne 0) {
    Write-Error "keystone-admin tests failed."
    exit $LASTEXITCODE
}

Write-Host "`n[SUCCESS] All quality constraints and tests verified successfully!" -ForegroundColor Green
