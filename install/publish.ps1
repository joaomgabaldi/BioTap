#Requires -Version 5.1
# Publica servico e app em $Out (servico na raiz, app em $Out\App) e confere o resultado.
# Usado pelo install.ps1 e pelo build-installer.ps1.
param([Parameter(Mandatory)][string]$Out)
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$app  = Join-Path $Out 'App'

# Self-contained: o instalador nao depende de o .NET estar instalado no PC
dotnet publish (Join-Path $repo 'src\BioTap.Service') -c Release -r win-x64 --self-contained -o $Out
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish (servico) falhou.' }
dotnet publish (Join-Path $repo 'src\BioTap.App') -c Release -o $app
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish (app) falhou.' }
# Sem o BioTap.pri (recursos + XAML compilado) a janela de preferencias derruba o app (fail-fast nativo)
if (-not (Test-Path (Join-Path $app 'BioTap.pri'))) { throw 'O publish do app saiu sem BioTap.pri (EnableMsixTooling no csproj?).' }
# O app roda elevado com o ambiente do usuario: precisa ser self-contained (ignora DOTNET_ROOT) e sem startup hooks
if (-not (Test-Path (Join-Path $app 'hostfxr.dll'))) { throw 'O publish do app nao saiu self-contained (SelfContained no csproj?).' }
$rc = Get-Content (Join-Path $app 'BioTap.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($rc.runtimeOptions.configProperties.'System.StartupHookProvider.IsSupported' -ne $false) { throw 'O publish do app aceita DOTNET_STARTUP_HOOKS (StartupHookSupport no csproj?).' }
