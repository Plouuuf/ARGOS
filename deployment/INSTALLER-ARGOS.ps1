<#
  Installation du serveur ARGOS sur le VPS VDMS (Windows), à côté de SIMCALL.

  À lancer depuis le dossier qui contient ARGOS.Server.exe, en administrateur
  (le plus simple : double-clic sur INSTALLER-ARGOS.cmd).

  Ce que fait le script :
    1. reprend l'application Discord de SIMCALL (Client ID, secret, serveur Discord)
       depuis C:\SIMCALL\Server\appsettings.json ;
    2. crée appsettings.Local.json (clé du serveur générée une fois, puis conservée) ;
    3. ouvre le port 8092 dans le pare-feu Windows ;
    4. installe une tâche planifiée qui démarre ARGOS au démarrage du VPS et le relance s'il s'arrête ;
    5. démarre ARGOS et vérifie http://127.0.0.1:8092/health ;
    6. relie SIMCALL à ARGOS (section Argos de C:\SIMCALL\Server\appsettings.json) : au décroché
       d'un 18, la prise d'appel s'ouvre sur le poste ARGOS de l'opérateur (nécessite SIMCALL 0.7.2 ou plus).

  Relançable sans risque : les réglages existants sont conservés.
#>
param(
    # Rôle Discord obligatoire pour entrer dans ARGOS (whitelist BSPP).
    # Par défaut : le rôle du standard 18 de SIMCALL.
    [string]$RequiredRoleId = "1560234256226193528",
    [string]$OperateurRoleIds = "1560234256226193528",
    [string]$EncadrementRoleIds = "",
    [string]$AdministrationRoleIds = "",
    [string]$PublicBaseUrl = "http://46.105.78.32:8092",
    [int]$Port = 8092
)

$ErrorActionPreference = "Stop"
$Root = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
$Exe = Join-Path $Root "ARGOS.Server.exe"
$LocalSettings = Join-Path $Root "appsettings.Local.json"
$SimcallSettings = "C:\SIMCALL\Server\appsettings.json"
$TaskName = "ARGOS Server"

function Step($text) { Write-Host ""; Write-Host "== $text" -ForegroundColor Cyan }
function Ok($text) { Write-Host "   OK  $text" -ForegroundColor Green }
function Warn($text) { Write-Host "   !!  $text" -ForegroundColor Yellow }
function Useful($value) { -not [string]::IsNullOrWhiteSpace($value) -and -not ([string]$value).StartsWith("CHANGE_ME") }

# --- Administrateur ---
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Lance ce script en administrateur (clic droit sur INSTALLER-ARGOS.cmd > Exécuter en tant qu'administrateur)."
}
if (-not (Test-Path $Exe)) { throw "ARGOS.Server.exe introuvable dans $Root. Place ce script dans le dossier du serveur ARGOS." }

Write-Host "Installation du serveur ARGOS dans $Root" -ForegroundColor White

# Le port doit être libre (ou déjà tenu par ARGOS lui-même).
$listener = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
if ($listener) {
    $owner = Get-Process -Id $listener.OwningProcess -ErrorAction SilentlyContinue
    if ($owner -and $owner.ProcessName -ne "ARGOS.Server") {
        throw "Le port $Port est déjà utilisé par « $($owner.ProcessName) ». Relance avec un autre port, ex. : INSTALLER-ARGOS.cmd -Port 8092 -PublicBaseUrl http://46.105.78.32:8092"
    }
}

# --- 1. Discord (repris de SIMCALL) ---
Step "Application Discord"
$existing = $null
if (Test-Path $LocalSettings) {
    $existing = Get-Content $LocalSettings -Raw | ConvertFrom-Json
    Ok "Réglages ARGOS existants trouvés : ils sont conservés."
}

$clientId = $existing.Discord.ClientId
$clientSecret = $existing.Discord.ClientSecret
$guildId = $existing.Discord.GuildId
if (-not (Useful $clientId) -or -not (Useful $clientSecret)) {
    if (-not (Test-Path $SimcallSettings)) { throw "$SimcallSettings introuvable : impossible de reprendre l'application Discord de SIMCALL." }
    $simcall = Get-Content $SimcallSettings -Raw | ConvertFrom-Json
    $clientId = $simcall.Discord.ClientId
    $clientSecret = $simcall.Discord.ClientSecret
    if (-not (Useful $guildId)) { $guildId = $simcall.Discord.GuildId }
    if (-not (Useful $clientId) -or -not (Useful $clientSecret)) { throw "Discord n'est pas configuré dans SIMCALL ($SimcallSettings)." }
    Ok "Application Discord reprise de SIMCALL (Client ID $clientId)."
}
if (-not (Useful $guildId)) { $guildId = "1342566186835251210" }

# --- 2. appsettings.Local.json ---
Step "Réglages du serveur"
$accessKey = $existing.ARGOS.AccessKey
if (-not (Useful $accessKey)) {
    $bytes = New-Object byte[] 48
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $accessKey = [Convert]::ToBase64String($bytes)
    Ok "Nouvelle clé de serveur générée."
}

# Les rôles déjà configurés à la main restent prioritaires sur les valeurs par défaut.
if ($existing -and (Useful $existing.Discord.RequiredRoleId) -and -not $PSBoundParameters.ContainsKey("RequiredRoleId")) {
    $RequiredRoleId = $existing.Discord.RequiredRoleId
}
$levels = [ordered]@{
    operateur = $OperateurRoleIds
    encadrement = $EncadrementRoleIds
    administration = $AdministrationRoleIds
}
if ($existing -and $existing.Discord.LevelRoles) {
    foreach ($name in @("operateur", "encadrement", "administration")) {
        $param = @{ operateur = "OperateurRoleIds"; encadrement = "EncadrementRoleIds"; administration = "AdministrationRoleIds" }[$name]
        $old = $existing.Discord.LevelRoles.$name
        if ((Useful $old) -and -not $PSBoundParameters.ContainsKey($param)) { $levels[$name] = $old }
    }
}

$settings = [ordered]@{
    Urls = "http://0.0.0.0:$Port"
    ARGOS = [ordered]@{ AccessKey = $accessKey }
    Discord = [ordered]@{
        ClientId = $clientId
        ClientSecret = $clientSecret
        PublicBaseUrl = $PublicBaseUrl
        GuildId = $guildId
        RequiredRoleId = $RequiredRoleId
        LevelRoles = $levels
    }
}
$settings | ConvertTo-Json -Depth 5 | Set-Content -Path $LocalSettings -Encoding utf8
Ok "appsettings.Local.json écrit (rôle requis : $RequiredRoleId)."

# Le fichier contient des secrets : lecture réservée aux administrateurs et au système.
try {
    icacls $LocalSettings /inheritance:r /grant:r "*S-1-5-32-544:(F)" "*S-1-5-18:(F)" | Out-Null
    Ok "Accès au fichier réservé aux administrateurs."
} catch { Warn "Impossible de restreindre les droits du fichier." }

# --- 2 bis. Lien SIMCALL -> ARGOS (prise d'appel au décroché d'un 18) ---
Step "Lien avec SIMCALL"
if (Test-Path $SimcallSettings) {
    try {
        $sc = Get-Content $SimcallSettings -Raw | ConvertFrom-Json
        $argosLink = [pscustomobject]@{ Url = "http://127.0.0.1:$Port"; Key = $accessKey }
        if ($sc.PSObject.Properties['Argos']) { $sc.Argos = $argosLink } else { $sc | Add-Member -NotePropertyName Argos -NotePropertyValue $argosLink }
        Copy-Item $SimcallSettings "$SimcallSettings.avant-argos" -Force
        $sc | ConvertTo-Json -Depth 20 | Set-Content -Path $SimcallSettings -Encoding utf8
        Ok "SIMCALL préviendra ARGOS au décroché des appels 18 (sauvegarde : appsettings.json.avant-argos)."
        $svc = Get-Service -Name "SIMCALLServer" -ErrorAction SilentlyContinue
        if ($svc -and $svc.Status -eq "Running") {
            Restart-Service -Name "SIMCALLServer" -Force
            Ok "Serveur SIMCALL redémarré pour prendre en compte le lien."
        } else {
            Warn "Service SIMCALL non trouvé ou arrêté : le lien sera actif à son prochain démarrage."
        }
    } catch {
        Warn "Impossible de configurer le lien dans SIMCALL : $($_.Exception.Message)"
    }
} else {
    Warn "SIMCALL introuvable : lien non configuré."
}

# --- 3. Pare-feu ---
Step "Pare-feu Windows (port $Port)"
$rule = Get-NetFirewallRule -DisplayName "ARGOS $Port" -ErrorAction SilentlyContinue
if ($rule) { Ok "Règle déjà présente." }
else {
    New-NetFirewallRule -DisplayName "ARGOS $Port" -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow | Out-Null
    Ok "Port $Port ouvert."
}

# --- 4. Démarrage automatique ---
Step "Démarrage automatique"
Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
Get-Process -Name "ARGOS.Server" -ErrorAction SilentlyContinue | Stop-Process -Force
$action = New-ScheduledTaskAction -Execute $Exe -WorkingDirectory $Root
$trigger = New-ScheduledTaskTrigger -AtStartup
$taskSettings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero) -StartWhenAvailable
$taskPrincipal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $taskSettings -Principal $taskPrincipal -Force | Out-Null
Ok "Tâche « $TaskName » installée (démarre avec le VPS, relancée en cas d'arrêt)."

# --- 5. Démarrage et vérification ---
Step "Démarrage"
Start-ScheduledTask -TaskName $TaskName
$healthy = $false
for ($i = 0; $i -lt 20 -and -not $healthy; $i++) {
    Start-Sleep -Seconds 1
    try {
        $health = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/health" -TimeoutSec 3
        $healthy = $health.status -eq "ok"
    } catch { }
}
if ($healthy) { Ok "ARGOS répond sur http://127.0.0.1:$Port/health" }
else { Warn "ARGOS ne répond pas encore. Lance ARGOS.Server.exe à la main pour voir le message d'erreur." }

Write-Host ""
Write-Host "Reste à faire une seule fois, dans le portail développeur Discord (application de SIMCALL) :" -ForegroundColor White
Write-Host "  OAuth2 > Redirects > ajouter : $PublicBaseUrl/auth/discord/callback" -ForegroundColor Yellow
Write-Host ""
Write-Host "Clé du serveur (pour les liens SIMCALL / eFIBI, en-tête X-Argos-Key) : dans $LocalSettings" -ForegroundColor DarkGray
