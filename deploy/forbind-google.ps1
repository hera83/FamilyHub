<#
.SYNOPSIS
    Forbinder en Google-konto til Family Hub på serveren – fra den bærbare.

.DESCRIPTION
    Google sender kun svaret fra loginsiden tilbage til "localhost", og køkkenskærmen har intet tastatur til
    Googles side. Scriptet åbner derfor en sikker SSH-tunnel til serveren, så den bærbares browser ser
    Family Hub på http://localhost:5099 – præcis som om den kørte på den bærbare.

    1. Åbner tunnelen og kalenderens indstillinger i browseren.
    2. Du trykker »Forbind Google-konto« og logger ind. Tryk Enter i terminalen, når du er færdig.

    Google-nøglerne (GOOGLE_CLIENT_ID og GOOGLE_CLIENT_SECRET) skal stå i .env på serveren først.
    Se docs/google-kalender.md for hele opsætningen.

.EXAMPLE
    .\deploy\forbind-google.ps1 -Server homelab.local -User heine
    Forbind en konto, flere konti, eller forbind en konto igen.
#>
param(
    [string]$Server = "homelab.local",
    [string]$User = $env:USERNAME,
    [int]$LocalPort = 5099
)

$ErrorActionPreference = "Stop"
$target = "$User@$Server"
$serverPort = 8080   # fast i docker-compose.yml

Write-Host "==> Åbner tunnel: http://localhost:$LocalPort -> Family Hub på $Server" -ForegroundColor Cyan
$tunnel = Start-Process ssh -ArgumentList "-N", "-o", "ExitOnForwardFailure=yes", "-L", "${LocalPort}:localhost:${serverPort}", $target -PassThru -NoNewWindow
try {
    $ready = $false
    for ($i = 0; $i -lt 60 -and -not $tunnel.HasExited; $i++) {
        try {
            Invoke-WebRequest "http://localhost:$LocalPort/health" -UseBasicParsing -TimeoutSec 2 | Out-Null
            $ready = $true
            break
        }
        catch {
            Start-Sleep -Seconds 1
        }
    }
    if (-not $ready) { throw "Family Hub svarer ikke gennem tunnelen. Kører containeren ('ssh $target docker compose -f familyhub/docker-compose.yml ps')?" }

    Start-Process "http://localhost:$LocalPort/kalender/indstillinger"
    Write-Host "    Browseren er åbnet. Tryk »Forbind Google-konto« og log ind hos Google."
    Read-Host "    Tryk Enter her, når kontoen står på listen"
}
finally {
    if (-not $tunnel.HasExited) { Stop-Process -Id $tunnel.Id }
}

Write-Host "==> Færdig. Køkkenskærmen henter kalenderne af sig selv." -ForegroundColor Green
