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

# Launcher sem admin dos atalhos (Native AOT). O link do AOT chama o vswhere, que fica fora do PATH
$vsInstaller = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if ((Test-Path $vsInstaller) -and ($env:PATH -notlike "*$vsInstaller*")) { $env:PATH += ";$vsInstaller" }
dotnet publish (Join-Path $repo 'src\BioTap.Launcher') -c Release -o $Out
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish (launcher) falhou.' }
# AOT de verdade: um exe só, sem a DLL gerenciada ao lado (senão faltaria o runtime no PC)
if (-not (Test-Path (Join-Path $Out 'BioTapLauncher.exe')) -or (Test-Path (Join-Path $Out 'BioTapLauncher.dll'))) { throw 'O publish do launcher nao saiu em Native AOT.' }
# O manifest embutido (texto no recurso RT_MANIFEST) tem de pedir asInvoker: senao o atalho voltaria a pedir UAC
$launcherBytes = [IO.File]::ReadAllText((Join-Path $Out 'BioTapLauncher.exe'), [Text.Encoding]::GetEncoding(28591))
if ($launcherBytes -notmatch 'level="asInvoker"' -or $launcherBytes -match 'requireAdministrator|highestAvailable') { throw 'O launcher saiu sem o manifest asInvoker.' }
