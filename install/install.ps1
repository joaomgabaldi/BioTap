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

if (Get-Service $svc -ErrorAction SilentlyContinue) {
    sc.exe config $svc binPath= "`"$exe`"" start= auto obj= LocalSystem | Out-Null
} else {
    sc.exe create $svc binPath= "`"$exe`"" start= auto obj= LocalSystem | Out-Null
}
if ($LASTEXITCODE -ne 0) { throw 'sc.exe falhou ao registrar o servico.' }
Start-Service $svc
Get-Service $svc
