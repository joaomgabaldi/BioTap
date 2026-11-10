#Requires -Version 5.1
# Publica o BioTap e gera o instalador em artifacts\installer\BioTap-Setup-<versao>.exe. Nao precisa de admin.
param([string]$Iscc = (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'))
$ErrorActionPreference = 'Stop'

$publish = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\publish'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force } # sem sobras de publishes antigos
& (Join-Path $PSScriptRoot 'publish.ps1') -Out $publish

& $Iscc "/DPublishDir=$publish" (Join-Path $PSScriptRoot 'BioTap.iss')
if ($LASTEXITCODE -ne 0) { throw 'ISCC falhou.' }
