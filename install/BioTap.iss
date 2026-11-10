; Instalador do BioTap (Inno Setup 7). Gere com install\build-installer.ps1, que publica antes em
; artifacts\publish e passa /DPublishDir. Os passos de sistema (servico, tarefa de logon, ACL do
; ProgramData) ficam no setup.ps1, o mesmo que o install.ps1 usa.

#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif
; Versão definida no Directory.Build.props: "1.0.1" (produto) e "1.0.1.0" (arquivo)
#define AppVersion GetStringFileInfo(PublishDir + "\App\BioTap.exe", "ProductVersion")
#define FileVersion GetVersionNumbersString(PublishDir + "\App\BioTap.exe")

[Setup]
AppId={{9FF42EA0-7445-4A0C-AC2B-9823D8E22538}
AppName=BioTap
AppVersion={#AppVersion}
AppVerName=BioTap
AppPublisher=João M. B. Gabaldi
AppPublisherURL=https://github.com/joaomgabaldi/BioTap
AppSupportURL=https://github.com/joaomgabaldi/BioTap/issues
AppUpdatesURL=https://github.com/joaomgabaldi/BioTap/releases
AppCopyright=Copyright © 2026 joaomgabaldi
VersionInfoVersion={#FileVersion}
VersionInfoProductVersion={#FileVersion}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoProductName=BioTap
VersionInfoCompany=João M. B. Gabaldi
VersionInfoCopyright=Copyright © 2026 joaomgabaldi
VersionInfoDescription=Instalador do BioTap
; O app e o servico procuram os arquivos em %ProgramFiles%\BioTap: a pasta nao pode mudar
DefaultDirName={autopf}\BioTap
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
; Servico e app sao parados pelo PrepareToInstall, antes de trocar os arquivos
CloseApplications=no
RestartApplications=no
WizardStyle=modern dark
SetupIconFile=..\src\BioTap.App\Assets\BioTap.ico
UninstallDisplayIcon={app}\App\BioTap.exe
UninstallDisplayName=BioTap
OutputDir=..\artifacts\installer
OutputBaseFilename=BioTap-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
SetupLogging=yes

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
; Idioma do app: o setup grava em HKLM; o app usa até o usuário escolher outro em Preferências
ptbr.LanguageCode=pt-BR
en.LanguageCode=en
ptbr.LogHint=Detalhes no log do instalador (%TEMP%\Setup Log *.txt).
en.LogHint=Details in the setup log (%TEMP%\Setup Log *.txt).
ptbr.StopFailed=Não foi possível parar o serviço ou o app do BioTap.
en.StopFailed=Could not stop the BioTap service or app.
ptbr.ConfigureFailed=Os arquivos foram instalados, mas registrar o serviço e a tarefa de logon falhou.
en.ConfigureFailed=The files were installed, but registering the service and the logon task failed.
ptbr.RemoveFailed=Não foi possível remover o serviço ou a tarefa de logon do BioTap.
en.RemoveFailed=Could not remove the BioTap service or the logon task.
ptbr.DeleteData=Apagar também as ações configuradas e os logs (%1)?
en.DeleteData=Also delete the configured actions and the logs (%1)?

[Registry]
; Só admin escreve em HKLM: um processo sem admin não muda o idioma do app (spec da tradução §6)
Root: HKLM; Subkey: "Software\BioTap"; ValueType: string; ValueName: "Language"; ValueData: "{cm:LanguageCode}"; Flags: uninsdeletekey

[InstallDelete]
; Atualizacao: sem DLLs de versoes antigas misturadas com as novas
Type: filesandordirs; Name: "{app}\App"
; Sobra do install.ps1 antigo (servico dependente de framework); o self-contained nao usa
Type: filesandordirs; Name: "{app}\runtimes"

[Files]
; Sem PDBs: guardam o caminho local da máquina de build (mapa do SourceLink)
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "setup.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Os atalhos abrem o launcher sem admin: com o app aberto, mostra o painel sem UAC (o BioTap.exe pede UAC)
Name: "{autoprograms}\BioTap"; Filename: "{app}\BioTapLauncher.exe"; IconFilename: "{app}\App\BioTap.exe"
Name: "{autodesktop}\BioTap"; Filename: "{app}\BioTapLauncher.exe"; IconFilename: "{app}\App\BioTap.exe"

[Code]
function PowerShell(const Args: String): Boolean;
var
  Code: Integer;
begin
  Log('powershell ' + Args);
  // {sysnative}: o PowerShell 64 bits; o de 32 bits veria %ProgramFiles% = Program Files (x86)
  Result := ExecAndLogOutput(ExpandConstant('{sysnative}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass ' + Args, '', SW_HIDE, ewWaitUntilTerminated, Code, nil)
    and (Code = 0);
end;

function SetupScript(const Mode: String): Boolean;
begin
  // {app} fica em Program Files (so admin escreve); nunca roda script de uma pasta temporaria
  Result := PowerShell('-File "' + ExpandConstant('{app}\setup.ps1') + '" -Mode ' + Mode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  // Comando inline: na primeira instalacao o setup.ps1 ainda nao existe em {app}
  if not PowerShell('-Command "Stop-Service BioTap.Service -Force -ErrorAction SilentlyContinue; ' +
      'Get-Process BioTap, BioTapLauncher -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep -Milliseconds 500; ' +
      'if (Get-Service BioTap.Service -ErrorAction SilentlyContinue | Where-Object Status -ne Stopped) { exit 1 }"') then
    Result := CustomMessage('StopFailed') + ' ' + CustomMessage('LogHint');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and not SetupScript('Configure') then
    SuppressibleMsgBox(CustomMessage('ConfigureFailed') + ' ' + CustomMessage('LogHint'),
      mbError, MB_OK, IDOK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Data: String;
begin
  // Depois da confirmação do usuário (InitializeUninstall roda antes dela) e antes de apagar os arquivos
  if (CurUninstallStep = usUninstall) and not SetupScript('Remove') then
    SuppressibleMsgBox(CustomMessage('RemoveFailed') + ' ' + CustomMessage('LogHint'),
      mbError, MB_OK, IDOK);
  Data := ExpandConstant('{commonappdata}\BioTap');
  if (CurUninstallStep = usPostUninstall) and DirExists(Data) and not UninstallSilent and
     (MsgBox(FmtMessage(CustomMessage('DeleteData'), [Data]), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
    DelTree(Data, True, True, True);
end;
