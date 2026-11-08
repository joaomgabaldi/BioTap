#Requires -Version 5.1
# Instala/atualiza o BioTap.Service. Rode num PowerShell como administrador.
$ErrorActionPreference = 'Stop'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { Write-Error 'Rode este script como administrador.'; exit 1 }

$repo = Split-Path -Parent $PSScriptRoot
$bin  = Join-Path $env:ProgramFiles 'BioTap'
$data = Join-Path $env:ProgramData 'BioTap'
$svc  = 'BioTap.Service'
$exe  = Join-Path $bin 'BioTap.Service.exe'

if (Get-Service $svc -ErrorAction SilentlyContinue) {
    Stop-Service $svc -Force -ErrorAction SilentlyContinue
    (Get-Service $svc).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}

dotnet publish (Join-Path $repo 'src\BioTap.Service') -c Release -o $bin
if ($LASTEXITCODE -ne 0) { Write-Error 'dotnet publish falhou.'; exit 1 }

# Program Files herda ACL so-admin; ProgramData nao, entao a ACL e explicita
New-Item -ItemType Directory -Force $data | Out-Null
icacls $data /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
icacls $data /setowner '*S-1-5-32-544' /T | Out-Null

if (-not (Get-Service $svc -ErrorAction SilentlyContinue)) {
    sc.exe create $svc binPath= "`"$exe`"" start= auto obj= LocalSystem | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Error 'sc.exe create falhou.'; exit 1 }
}
Start-Service $svc
Get-Service $svc
