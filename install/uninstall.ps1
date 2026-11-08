#Requires -Version 5.1
# Remove o BioTap.Service. Rode num PowerShell como administrador.
$ErrorActionPreference = 'Stop'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { Write-Error 'Rode este script como administrador.'; exit 1 }

$svc = 'BioTap.Service'
if (Get-Service $svc -ErrorAction SilentlyContinue) {
    Stop-Service $svc -Force -ErrorAction SilentlyContinue
    sc.exe delete $svc | Out-Null
}
Remove-Item (Join-Path $env:ProgramFiles 'BioTap') -Recurse -Force -ErrorAction SilentlyContinue

$data = Join-Path $env:ProgramData 'BioTap'
if ((Test-Path $data) -and (Read-Host "Apagar tambem a configuracao em $data? (s/N)") -eq 's') {
    Remove-Item $data -Recurse -Force
}
