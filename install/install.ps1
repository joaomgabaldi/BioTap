#Requires -Version 5.1
#Requires -RunAsAdministrator
# Instala/atualiza o BioTap.Service. Rode num PowerShell como administrador.
# Nota: roda `dotnet publish` como admin sobre o repo; so use num repo em que voce confia.
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$bin  = Join-Path $env:ProgramFiles 'BioTap'
$data = Join-Path $env:ProgramData 'BioTap'
$svc  = 'BioTap.Service'
$exe  = Join-Path $bin 'BioTap.Service.exe'

$appExe = Join-Path $bin 'App\BioTap.exe'
$task  = 'BioTap'
$user  = [Security.Principal.WindowsIdentity]::GetCurrent().Name

if (Get-Service $svc -ErrorAction SilentlyContinue) {
    Stop-Service $svc -Force
    (Get-Service $svc).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}
Get-Process BioTap -ErrorAction SilentlyContinue | Stop-Process -Force # app files are replaced below

dotnet publish (Join-Path $repo 'src\BioTap.Service') -c Release -o $bin
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish (servico) falhou.' }
dotnet publish (Join-Path $repo 'src\BioTap.App') -c Release -o (Join-Path $bin 'App')
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish (app) falhou.' }
# Sem o BioTap.pri (recursos + XAML compilado) a janela de preferencias derruba o app (fail-fast nativo)
if (-not (Test-Path (Join-Path $bin 'App\BioTap.pri'))) { throw 'O publish do app saiu sem BioTap.pri (EnableMsixTooling no csproj?).' }
# O app roda elevado com o ambiente do usuario: precisa ser self-contained (ignora DOTNET_ROOT) e sem startup hooks
if (-not (Test-Path (Join-Path $bin 'App\hostfxr.dll'))) { throw 'O publish do app nao saiu self-contained (SelfContained no csproj?).' }
$rc = Get-Content (Join-Path $bin 'App\BioTap.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($rc.runtimeOptions.configProperties.'System.StartupHookProvider.IsSupported' -ne $false) { throw 'O publish do app aceita DOTNET_STARTUP_HOOKS (StartupHookSupport no csproj?).' }

# ProgramData e gravavel por usuarios comuns. Uma pasta BioTap ja existente so e reaproveitada se for confiavel
# (dona admin/SYSTEM, ninguem mais escreve); senao e renomeada para o lado e uma nova e criada. Nunca usa /T:
# icacls recursivo seguiria juncoes plantadas e mudaria ACLs de outras pastas.
function Test-TrustedDir([string]$path) {
    $item = Get-Item $path -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { return $false }
    $trusted = 'S-1-5-18', 'S-1-5-32-544'
    $acl = Get-Acl $path
    $owner = (New-Object Security.Principal.NTAccount $acl.Owner).Translate([Security.Principal.SecurityIdentifier]).Value
    if ($trusted -notcontains $owner) { return $false }
    $write = [Security.AccessControl.FileSystemRights]'WriteData, AppendData, WriteExtendedAttributes, WriteAttributes, Delete, DeleteSubdirectoriesAndFiles, ChangePermissions, TakeOwnership'
    foreach ($rule in $acl.Access) {
        if ($rule.AccessControlType -ne 'Allow' -or -not ($rule.FileSystemRights -band $write)) { continue }
        $sid = $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
        if ($trusted -notcontains $sid) { return $false }
    }
    return $true
}
if ((Test-Path $data) -and -not (Test-TrustedDir $data)) {
    $aside = "$data.untrusted-$(Get-Date -Format yyyyMMddHHmmss)"
    Rename-Item $data $aside   # renomear nao segue juncoes
    Write-Warning "$data nao era confiavel; movido para $aside"
}
if (-not (Test-Path $data)) {
    New-Item -ItemType Directory $data | Out-Null
    icacls $data /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' /Q | Out-Null
    icacls $data /setowner '*S-1-5-32-544' /Q | Out-Null
}
# Logs never hold secrets (no typed text, clipboard or command arguments), so users may READ them to diagnose
# problems without elevation; writing stays admin-only. The folder inherits the admin-only ACL plus this grant.
$logs = Join-Path $data 'logs'
if ((Test-Path $logs) -and ((Get-Item $logs -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "$logs e um ponto de reparse." }
New-Item -ItemType Directory -Force $logs | Out-Null
icacls $logs /grant '*S-1-5-32-545:(OI)(CI)RX' /Q | Out-Null

if (Get-Service $svc -ErrorAction SilentlyContinue) {
    sc.exe config $svc binPath= "`"$exe`"" start= auto obj= LocalSystem | Out-Null
} else {
    sc.exe create $svc binPath= "`"$exe`"" start= auto obj= LocalSystem | Out-Null
}
if ($LASTEXITCODE -ne 0) { throw 'sc.exe falhou ao registrar o servico.' }
Start-Service $svc

# The app runs elevated: a logon task with highest privileges starts it without a UAC prompt (spec §2, §6)
$action    = New-ScheduledTaskAction -Execute $appExe
$trigger   = New-ScheduledTaskTrigger -AtLogOn -User $user
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
$settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)
Register-ScheduledTask -TaskName $task -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
Start-ScheduledTask -TaskName $task # start it now in the user's session

Get-Service $svc
Get-ScheduledTask -TaskName $task | Select-Object TaskName, State
