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

if (Get-Service $svc -ErrorAction SilentlyContinue) {
    Stop-Service $svc -Force
    (Get-Service $svc).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}

dotnet publish (Join-Path $repo 'src\BioTap.Service') -c Release -o $bin
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish falhou.' }

# ProgramData e gravavel por usuarios comuns: recusa juncoes plantadas e limpa ACLs herdadas/plantadas
if ((Test-Path $data) -and ((Get-Item $data -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw "$data e um ponto de reparse (juncao/link). Remova-o manualmente e rode de novo."
}
New-Item -ItemType Directory -Force $data | Out-Null
icacls $data /reset /T /Q | Out-Null
icacls $data /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' /Q | Out-Null
icacls $data /setowner '*S-1-5-32-544' /T /Q | Out-Null

if (Get-Service $svc -ErrorAction SilentlyContinue) {
    sc.exe config $svc binPath= "`"$exe`"" start= auto obj= LocalSystem | Out-Null
} else {
    sc.exe create $svc binPath= "`"$exe`"" start= auto obj= LocalSystem | Out-Null
}
if ($LASTEXITCODE -ne 0) { throw 'sc.exe falhou ao registrar o servico.' }
Start-Service $svc
Get-Service $svc
