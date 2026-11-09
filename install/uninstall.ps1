#Requires -Version 5.1
#Requires -RunAsAdministrator
# Remove o BioTap.Service. Rode num PowerShell como administrador.
$ErrorActionPreference = 'Stop'

$svc = 'BioTap.Service'
if (Get-ScheduledTask -TaskName 'BioTap' -ErrorAction SilentlyContinue) { Unregister-ScheduledTask -TaskName 'BioTap' -Confirm:$false }
Get-Process BioTap -ErrorAction SilentlyContinue | Stop-Process -Force
if (Get-Service $svc -ErrorAction SilentlyContinue) {
    Stop-Service $svc -Force
    (Get-Service $svc).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    sc.exe delete $svc | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'sc.exe delete falhou.' }
}

$bin = Join-Path $env:ProgramFiles 'BioTap'
if (Test-Path $bin) { Remove-Item $bin -Recurse -Force }

$data = Join-Path $env:ProgramData 'BioTap'
if ((Test-Path $data) -and (Read-Host "Apagar tambem a configuracao em $data? (s/N)") -eq 's') {
    Remove-Item $data -Recurse -Force
}
