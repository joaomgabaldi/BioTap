#Requires -Version 5.1
#Requires -RunAsAdministrator
# Remove o BioTap instalado pelo install.ps1. Rode num PowerShell como administrador.
# (Se veio do instalador, desinstale por Configuracoes > Aplicativos.)
$ErrorActionPreference = 'Stop'

& (Join-Path $PSScriptRoot 'setup.ps1') -Mode Remove

$bin = Join-Path $env:ProgramFiles 'BioTap'
if (Test-Path $bin) { Remove-Item $bin -Recurse -Force }

$data = Join-Path $env:ProgramData 'BioTap'
if ((Test-Path $data) -and (Read-Host "Apagar tambem a configuracao em $data? (s/N)") -eq 's') {
    Remove-Item $data -Recurse -Force
}
