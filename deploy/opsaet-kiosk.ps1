<#
.SYNOPSIS
    Gør en Raspberry Pi til køkkenskærm for Family Hub – fra den bærbare, over SSH.

.DESCRIPTION
    Pi'en kører kun Chromium i kiosktilstand. Selve Family Hub kører i Docker på serveren.
    1. Kopierer deploy/kiosk til Pi'en.
    2. Kører install.sh: Chromium, serverens adresse, kiosk ved login, ingen skærmslukning.
       En gammel Family Hub-server på Pi'en bliver stoppet (data bliver liggende).
    3. Genstart Pi'en bagefter, så kiosken starter.

    Kan køres igen uden skade – fx for at skifte til en anden server.

.EXAMPLE
    .\deploy\opsaet-kiosk.ps1 -ServerUrl http://homelab.local:8080
#>
param(
    [Parameter(Mandatory)]
    [string]$ServerUrl,
    [string]$PiHost = "familyhub.local",
    [string]$PiUser = "pi"
)

$ErrorActionPreference = "Stop"
if ($ServerUrl -notmatch '^https?://') { throw "Angiv serverens adresse med http://, fx http://homelab.local:8080" }

Write-Host "==> Tjekker, at Family Hub svarer på $ServerUrl" -ForegroundColor Cyan
try {
    Invoke-WebRequest "$($ServerUrl.TrimEnd('/'))/health" -UseBasicParsing -TimeoutSec 5 | Out-Null
    Write-Host "    Family Hub svarer."
}
catch {
    Write-Warning "Family Hub svarer ikke fra den bærbare. Kiosken venter selv, til serveren svarer – fortsætter."
}

$target = "$PiUser@$PiHost"
Write-Host "==> Kopierer kiosk-filerne til $target" -ForegroundColor Cyan
ssh $target "rm -rf /tmp/familyhub-kiosk"
if ($LASTEXITCODE -ne 0) { throw "Kan ikke logge ind med 'ssh $target'." }
scp -r (Join-Path $PSScriptRoot "kiosk") "${target}:/tmp/familyhub-kiosk"
if ($LASTEXITCODE -ne 0) { throw "scp fejlede." }

Write-Host "==> Sætter kiosken op" -ForegroundColor Cyan
ssh -t $target "sudo bash /tmp/familyhub-kiosk/install.sh $ServerUrl $PiUser; rm -rf /tmp/familyhub-kiosk"
if ($LASTEXITCODE -ne 0) { throw "Opsætningen på Pi'en fejlede - se beskeden ovenfor." }

Write-Host "==> Færdig. Genstart Pi'en:  ssh $target sudo reboot" -ForegroundColor Green
