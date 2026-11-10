#Requires -Version 5.1
#Requires -RunAsAdministrator
# Passos de instalacao compartilhados pelo install.ps1 (dev) e pelo instalador (BioTap.iss).
#   -Mode Stop       para o servico e fecha o app (antes de trocar os arquivos)
#   -Mode Configure  pasta de dados com ACL, servico automatico e tarefa de logon; abre o app
#   -Mode Remove     desfaz o Configure (nao apaga arquivos nem a configuracao)
param([Parameter(Mandatory)][ValidateSet('Stop', 'Configure', 'Remove')][string]$Mode)
$ErrorActionPreference = 'Stop'
# Em 32 bits, %ProgramFiles% e Program Files (x86): o servico seria registrado num caminho errado
if (-not [Environment]::Is64BitProcess) { throw 'Rode o setup.ps1 num PowerShell de 64 bits.' }

$bin    = Join-Path $env:ProgramFiles 'BioTap'
$data   = Join-Path $env:ProgramData 'BioTap'
$svc    = 'BioTap.Service'
$exe    = Join-Path $bin 'BioTap.Service.exe'
$appExe = Join-Path $bin 'App\BioTap.exe'
$task   = 'BioTap'

function Stop-BioTap {
    if (Get-Service $svc -ErrorAction SilentlyContinue) {
        Stop-Service $svc -Force
        (Get-Service $svc).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    }
    Get-Process BioTap -ErrorAction SilentlyContinue | Stop-Process -Force
}

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

function Install-BioTap {
    # Atualização: o serviço já existe. Sem a tarefa, o usuário desligou "Iniciar com o Windows"; essa escolha fica.
    $keepStartup = -not (Get-Service $svc -ErrorAction SilentlyContinue) -or (Get-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue)
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
        sc.exe config $svc binPath= $exe start= auto obj= LocalSystem | Out-Null
    } else {
        sc.exe create $svc binPath= $exe start= auto obj= LocalSystem DisplayName= 'BioTap' | Out-Null
    }
    if ($LASTEXITCODE -ne 0) { throw 'sc.exe falhou ao registrar o servico.' }
    # O PowerShell 5.1 tira as aspas internas ao chamar o sc.exe (medido: ImagePath ficava sem aspas, caminho com
    # espaco sem aspas e brecha classica de servico). Grava o caminho com aspas direto e confere.
    $key = "HKLM:\SYSTEM\CurrentControlSet\Services\$svc"
    Set-ItemProperty $key ImagePath "`"$exe`""
    if ((Get-ItemProperty $key).ImagePath -ne "`"$exe`"") { throw 'Caminho do servico sem aspas.' }
    Start-Service $svc

    if (-not $keepStartup) { Start-Process $appExe; return } # o instalador já roda elevado, como o app precisa

    # The app runs elevated: a logon task with highest privileges starts it without a UAC prompt (spec §2, §6)
    $user      = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $action    = New-ScheduledTaskAction -Execute $appExe
    $trigger   = New-ScheduledTaskTrigger -AtLogOn -User $user
    $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
    $settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)
    Register-ScheduledTask -TaskName $task -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
    Start-ScheduledTask -TaskName $task # start it now in the user's session
}

function Remove-BioTap {
    if (Get-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue) { Unregister-ScheduledTask -TaskName $task -Confirm:$false }
    Stop-BioTap
    if (Get-Service $svc -ErrorAction SilentlyContinue) {
        sc.exe delete $svc | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'sc.exe delete falhou.' }
    }
}

switch ($Mode) {
    'Stop'      { Stop-BioTap }
    'Configure' { Install-BioTap }
    'Remove'    { Remove-BioTap }
}
