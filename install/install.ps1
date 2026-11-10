#Requires -Version 5.1
#Requires -RunAsAdministrator
# Instala/atualiza o BioTap direto do repo (dev). Rode num PowerShell como administrador.
# Para distribuir, use o instalador: install\build-installer.ps1.
# Nota: roda `dotnet publish` como admin sobre o repo; so use num repo em que voce confia.
$ErrorActionPreference = 'Stop'

& (Join-Path $PSScriptRoot 'setup.ps1') -Mode Stop # app and service files are replaced below
& (Join-Path $PSScriptRoot 'publish.ps1') -Out (Join-Path $env:ProgramFiles 'BioTap')
& (Join-Path $PSScriptRoot 'setup.ps1') -Mode Configure

Get-Service BioTap.Service
Get-ScheduledTask -TaskName BioTap | Select-Object TaskName, State
