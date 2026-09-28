<#
.SYNOPSIS
    Tester Family Hub og starter den nye version i Docker på serveren – over SSH.

.DESCRIPTION
    1. Kører alle tests (intet sendes, hvis en test fejler).
    2. Pakker kildekoden – kun det, Docker skal bruge. Aldrig .env, secrets, data eller bin/obj.
    3. Kopierer pakken til serveren og pakker den ud i -RemoteDir. .env og data på serveren røres ikke.
    4. Kører deploy/server/update.sh på serveren: docker compose up -d --build og venter på svar.

    Første gang oprettes .env på serveren ud fra .env.example. Udfyld den bagefter og kør scriptet igen
    (se README.md). Serveren skal have Docker med compose-plugin; imaget bygges på serveren.

.EXAMPLE
    .\deploy\publish-server.ps1 -Server homelab.local -User heine
    Første installation og alle opdateringer. Køkkenskærmen genindlæser selv.

.EXAMPLE
    .\deploy\publish-server.ps1 -SkipDeploy
    Byg kun pakken (artifacts\familyhub-src.tar.gz).
#>
param(
    [string]$Server = "homelab.local",
    [string]$User = $env:USERNAME,
    # Mappe på serveren – relativ til brugerens hjemmemappe, eller en absolut sti.
    [string]$RemoteDir = "familyhub",
    [switch]$SkipTests,
    [switch]$SkipDeploy
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$archive = Join-Path $root "artifacts\familyhub-src.tar.gz"

if (-not $SkipTests) {
    Write-Host "==> Kører tests" -ForegroundColor Cyan
    dotnet test (Join-Path $root "FamilyHub.slnx") --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Tests fejlede - intet er sendt til serveren." }
}

Write-Host "==> Pakker kildekoden" -ForegroundColor Cyan
New-Item -ItemType Directory -Force (Split-Path $archive) | Out-Null
$include = @(
    "src", "deploy/server", "Dockerfile", "docker-compose.yml", ".dockerignore", ".env.example",
    "global.json", "Directory.Build.props", "Directory.Packages.props"
)
# --no-fflags/--no-xattrs: Windows-filattributter giver ellers advarsler fra tar på Linux.
tar -czf $archive -C $root --no-fflags --no-xattrs --exclude bin --exclude obj --exclude node_modules --exclude "*.user" @include
if ($LASTEXITCODE -ne 0) { throw "Kunne ikke pakke filerne." }
Write-Host ("    Pakke: {0} ({1:N1} MB)" -f $archive, ((Get-Item $archive).Length / 1MB))

if ($SkipDeploy) { return }

$target = "$User@$Server"
$dir = if ($RemoteDir.StartsWith("/")) { $RemoteDir } else { "`$HOME/$RemoteDir" }

Write-Host "==> Kopierer til $target" -ForegroundColor Cyan
scp $archive "${target}:/tmp/familyhub-src.tar.gz"
if ($LASTEXITCODE -ne 0) { throw "scp fejlede - kan du logge ind med 'ssh $target'?" }

Write-Host "==> Bygger og starter på serveren (første gang tager det et par minutter)" -ForegroundColor Cyan
# src slettes først, så filer, der er fjernet lokalt, også forsvinder på serveren. .env og data bliver.
ssh -t $target "set -e; mkdir -p $dir; rm -rf $dir/src; tar -xzf /tmp/familyhub-src.tar.gz -C $dir; rm -f /tmp/familyhub-src.tar.gz; bash $dir/deploy/server/update.sh"
if ($LASTEXITCODE -ne 0) { throw "Opdateringen på serveren fejlede - se beskeden ovenfor." }

Write-Host "==> Færdig" -ForegroundColor Green
