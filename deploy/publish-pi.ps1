<#
.SYNOPSIS
    Tester, bygger og sender Family Hub til Raspberry Pi'en over SSH.

.DESCRIPTION
    1. Kører alle tests (intet sendes, hvis en test fejler).
    2. Bygger en selvstændig linux-arm64-udgave (Pi'en behøver ikke .NET installeret).
    3. Pakker den som én .tar.gz og kopierer den til Pi'en med scp.
    4. Kører install.sh (første gang, -Install) eller update.sh på Pi'en.

.EXAMPLE
    .\deploy\publish-pi.ps1 -PiHost familyhub.local -PiUser pi -Install
    Første installation på en frisk Raspberry Pi.

.EXAMPLE
    .\deploy\publish-pi.ps1 -PiHost familyhub.local
    Opdatering. Køkkenskærmen genindlæser selv.

.EXAMPLE
    .\deploy\publish-pi.ps1 -SkipDeploy
    Byg kun pakken (artifacts\familyhub-linux-arm64.tar.gz).
#>
param(
    [string]$PiHost = "familyhub.local",
    [string]$PiUser = "pi",
    [switch]$Install,
    [switch]$SkipTests,
    [switch]$SkipDeploy
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root "artifacts\publish\linux-arm64"
$archive = Join-Path $root "artifacts\familyhub-linux-arm64.tar.gz"

if (-not $SkipTests) {
    Write-Host "==> Kører tests" -ForegroundColor Cyan
    dotnet test (Join-Path $root "FamilyHub.slnx") --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Tests fejlede - intet er sendt til Pi'en." }
}

Write-Host "==> Bygger til Raspberry Pi (linux-arm64)" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish (Join-Path $root "src\FamilyHub.Web\FamilyHub.Web.csproj") `
    -c Release -r linux-arm64 --self-contained true -o $publishDir `
    -p:DebugType=none --nologo
if ($LASTEXITCODE -ne 0) { throw "Build fejlede." }

Copy-Item (Join-Path $PSScriptRoot "raspberry-pi") (Join-Path $publishDir "deploy") -Recurse
New-Item -ItemType Directory -Force (Split-Path $archive) | Out-Null
tar -czf $archive -C $publishDir .
if ($LASTEXITCODE -ne 0) { throw "Kunne ikke pakke filerne." }
Write-Host ("    Pakke: {0} ({1:N0} MB)" -f $archive, ((Get-Item $archive).Length / 1MB))

if ($SkipDeploy) { return }

$target = "$PiUser@$PiHost"
Write-Host "==> Kopierer til $target" -ForegroundColor Cyan
scp $archive "${target}:/tmp/familyhub.tar.gz"
if ($LASTEXITCODE -ne 0) { throw "scp fejlede - kan du logge ind med 'ssh $target'?" }

$script = if ($Install) { "install.sh $PiUser" } else { "update.sh" }
Write-Host "==> Kører $script på Pi'en" -ForegroundColor Cyan
ssh -t $target "set -e; rm -rf /tmp/familyhub; mkdir -p /tmp/familyhub; tar -xzf /tmp/familyhub.tar.gz -C /tmp/familyhub; sudo bash /tmp/familyhub/deploy/$script; rm -rf /tmp/familyhub /tmp/familyhub.tar.gz"
if ($LASTEXITCODE -ne 0) { throw "Installationen på Pi'en fejlede - se beskeden ovenfor." }

Write-Host "==> Færdig" -ForegroundColor Green
