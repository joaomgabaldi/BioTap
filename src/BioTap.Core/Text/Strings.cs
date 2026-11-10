namespace BioTap.Core.Text;

/// <summary>
/// Todo texto que aparece na tela (spec da tradução §2). Um membro abstrato por texto: um idioma sem tradução não
/// compila. Ler <see cref="Current"/> na hora de mostrar; nunca guardar o texto traduzido.
/// </summary>
public abstract class Strings
{
    static readonly Strings PtBrInstance = new PtBr(), EnInstance = new En();

    public static Strings For(Language l) => l == Language.En ? EnInstance : PtBrInstance;

    /// <summary>Idioma em uso. Os testes não trocam (é estático e eles rodam em paralelo).</summary>
    public static Strings Current { get; set; } = PtBrInstance;

    public abstract Language Language { get; }

    // Preferências → Geral
    public abstract string LanguageLabel { get; }
    public string LanguagePtBr => "Português (Brasil)";
    public string LanguageEn => "English";

    // Core: presets, validação, configuração, painel e bandeja
    public abstract string PresetAudioCycle { get; }
    public abstract string PresetVolumeSet { get; }
    public abstract string PresetOpenTarget { get; }
    public abstract string PresetRunCommand { get; }
    public abstract string PresetShortcut { get; }
    public abstract string PresetVolumeUp { get; }
    public abstract string PresetVolumeDown { get; }
    public abstract string PresetTypeText { get; }
    public abstract string PresetQrCode { get; }
    public abstract string PresetScreenshot { get; }
    public abstract string PresetSequence { get; }
    public abstract string PresetTopmost { get; }
    public abstract string PresetMinimizeOthers { get; }
    public abstract string PresetLock { get; }
    public abstract string PresetCopy { get; }
    public abstract string PresetPaste { get; }
    public abstract string PresetMute { get; }
    public abstract string PresetPlayPause { get; }
    public abstract string AudioMinDevices { get; }
    public abstract string AudioMaxDevices { get; }
    public abstract string AudioInvalidDevice { get; }
    public abstract string AudioRepeatedDevice { get; }
    public abstract string AudioNoneConnected { get; }
    public abstract string AudioNoOtherConnected { get; }
    public abstract string AudioNoActiveOutput { get; }
    public abstract string VolumeRange { get; }
    public abstract string OpenChooseTarget { get; }
    public abstract string OpenPathTooLong { get; }
    public abstract string OpenInvalidPath { get; }
    public abstract string OpenInvalidArgs { get; }
    public abstract string OpenNoNetwork { get; }
    public abstract string OpenNeedFullPath { get; }
    public abstract string FileNotFound { get; }
    public abstract string CmdChooseShell { get; }
    public abstract string CmdWriteCommand { get; }
    public abstract string CmdTooLong { get; }
    public abstract string CmdInvalid { get; }
    public abstract string CmdOneLine { get; }
    public abstract string ShortcutInvalid { get; }
    public abstract string ShortcutChooseKey { get; }
    public abstract string ShortcutInvalidKey { get; }
    public abstract string ShortcutNoWinL { get; }
    public abstract string ShortcutNoCtrlAltDel { get; }
    public abstract string TypeWriteText { get; }
    public abstract string TypeTooLong { get; }
    public abstract string QrNoText { get; }
    public abstract string QrTooLong { get; }
    public abstract string QrCannotShow { get; }
    public abstract string FolderNotFound { get; }
    public abstract string FolderInvalid { get; }
    public abstract string ImageNotSaved { get; }
    public abstract string ShotSaveOrCopy { get; }
    public abstract string ShotChooseFolder { get; }
    public abstract string NoWindowFocused { get; }
    public abstract string WindowNotFound { get; }
    public abstract string SeqStepCount { get; }
    public abstract string SeqStepInvalid(int step);
    public abstract string SeqNoNested { get; }
    public abstract string SeqDelay { get; }
    public abstract string SeqStepBadParams(int step);
    public abstract string SeqStepUnknown(int step);
    public abstract string SeqStepLabel(int step, string preset);
    public abstract string TimedOut { get; }
    public abstract string RunError { get; }
    public abstract string ExplorerNotRunning { get; }
    public abstract string ExplorerOpenFailed { get; }
    public abstract string ExplorerNotFound { get; }
    public abstract string ExplorerNotVerifiable { get; }
    public abstract string ShellNotExplorer { get; }
    public abstract string ExplorerElevated { get; }
    public abstract string Taps(int n);
    public abstract string InternalError { get; }
    public abstract string NothingConfiguredFor(int n);
    public abstract string UnknownPreset { get; }
    public abstract string UnknownPresetDot { get; }
    public abstract string StillRunning { get; }
    public abstract string AlreadyUsedBy(string name);
    public abstract string ConfigNotSaved { get; }
    public abstract string SavedServiceNotNotified { get; }
    public abstract string ConfigInsecure { get; }
    public abstract string ConfigTooBig { get; }
    public abstract string ConfigNoVersion { get; }
    public abstract string ConfigVersionUnsupported(int version);
    public abstract string ConfigIncomplete { get; }
    public abstract string ConfigUnreadable(string error);
    public abstract string ConfigCannotOverwrite { get; }
    public abstract string TimeoutRange(int min, int max);
    public abstract string OverlayRange(int min, int max);
    public abstract string ActionBadId { get; }
    public abstract string TapsRange(int max);
    public abstract string TapsTaken(int taps, string name);
    public abstract string UnknownPresetNamed(string preset);
    public abstract string InvalidParams { get; }
    public abstract string PanelEmpty { get; }
    public abstract string StatusConnecting { get; }
    public abstract string StatusServiceMissing { get; }
    public abstract string StatusNotTrusted { get; }
    public abstract string StatusReady { get; }
    public abstract string StatusNoSensor { get; }
    public abstract string StatusNoFocus { get; }
    public abstract string StatusHello { get; }
    public abstract string StatusWaiting { get; }
    public abstract string StartupNeedsProgramFiles { get; }
    public abstract string StartupTaskDescription { get; }

    // App: preferências, editores, painel, bandeja e erros do sistema
    public abstract string SettingsTitle { get; }
    public abstract string SettingsSubtitle { get; }
    public abstract string WindowTitle { get; }
    public abstract string NavActions { get; }
    public abstract string NavGeneral { get; }
    public abstract string NavAppearance { get; }
    public abstract string SensorStatusName { get; }
    public abstract string SensorReady { get; }
    public abstract string BannerConfigReadOnly { get; }
    public abstract string BannerNotSaved { get; }
    public abstract string BannerSavedWithWarning { get; }
    public abstract string Save { get; }
    public abstract string Saved { get; }
    public abstract string Cancel { get; }
    public abstract string Edit { get; }
    public abstract string Delete { get; }
    public abstract string Test { get; }
    public abstract string ActionsDescription { get; }
    public abstract string YourActions { get; }
    public abstract string TapHint { get; }
    public abstract string CatOpenRun { get; }
    public abstract string CatKeyboard { get; }
    public abstract string CatSound { get; }
    public abstract string CatWindows { get; }
    public abstract string CatOther { get; }
    public abstract string NewAction { get; }
    public abstract string EditAction { get; }
    public abstract string InvalidOpenToFix { get; }
    public abstract string Disabled { get; }
    public abstract string TapSingular { get; }
    public abstract string TapPlural { get; }
    public abstract string EditNamed(string name, string taps);
    public abstract string TestNamed(string name);
    public abstract string EnableNamed(string name);
    public abstract string ActionOptions { get; }
    public abstract string OptionsOf(string name);
    public abstract string DeleteTitle(string name);
    public abstract string DeleteBody(string taps);
    public abstract string TapsHeader { get; }
    public abstract string TapsDescription { get; }
    public abstract string ActionName { get; }
    public abstract string ActionNameDescription { get; }
    public abstract string IconHeader { get; }
    public abstract string ProgramIcon { get; }
    public abstract string CurrentIcon { get; }
    public abstract string NoParams { get; }
    public abstract string SectionIdentification { get; }
    public abstract string SectionParameters { get; }
    public abstract string IconOpen { get; }
    public abstract string IconCommand { get; }
    public abstract string IconApps { get; }
    public abstract string IconFolder { get; }
    public abstract string IconSound { get; }
    public abstract string IconHeadphones { get; }
    public abstract string IconVideo { get; }
    public abstract string IconWeb { get; }
    public abstract string IconGame { get; }
    public abstract string IconLock { get; }
    public abstract string IconCamera { get; }
    public abstract string IconPlay { get; }
    public abstract string IconStar { get; }
    public abstract string IconFingerprint { get; }
    public abstract string GeneralDescription { get; }
    public abstract string StartWithWindows { get; }
    public abstract string StartWithWindowsDescription { get; }
    public abstract string TaskSchedulerUnreadable { get; }
    public abstract string StartupChangeFailed { get; }
    public abstract string TapInterval { get; }
    public abstract string TapIntervalDescription(int min, int max);
    public abstract string SectionStartup { get; }
    public abstract string SectionTapDetection { get; }
    public abstract string SectionLanguage { get; }
    public abstract string LanguageDescription { get; }
    public abstract string AppearanceDescription { get; }
    public abstract string SectionPreview { get; }
    public abstract string PreviewNamed(string title);
    public abstract string ShowConfirmation { get; }
    public abstract string ShowConfirmationDescription { get; }
    public abstract string TimeOnScreen { get; }
    public abstract string TimeOnScreenDescription(int min, int max);
    public abstract string AddStep { get; }
    public abstract string BackToSequence { get; }
    public abstract string WaitBefore { get; }
    public abstract string StepsHeader { get; }
    public abstract string WhatToCapture { get; }
    public abstract string Screen { get; }
    public abstract string CurrentWindow { get; }
    public abstract string SaveToFolder { get; }
    public abstract string Folder { get; }
    public abstract string Browse { get; }
    public abstract string CopyToClipboard { get; }
    public abstract string Command { get; }
    public abstract string ShowWindow { get; }
    public abstract string RunAsAdmin { get; }
    public abstract string OpenTargetHeader { get; }
    public abstract string OpenTargetPlaceholder { get; }
    public abstract string ArgsOptional { get; }
    public abstract string Key { get; }
    public abstract string VolumeStep { get; }
    public abstract string TextHeader { get; }
    public abstract string TextCounter(int n);
    public abstract string UnknownDevice { get; }
    public abstract string DisconnectedSuffix { get; }
    public abstract string CannotListDevices { get; }
    public abstract string DevicesCount(int n);
    public abstract string CaptureFailed { get; }
    public abstract string CopyImageFailed { get; }
    public abstract string KeysBlocked { get; }
    public abstract string WindowChangeFailed { get; }
    public abstract string UnnamedDevice { get; }
    public abstract string AudioSwitchFailed { get; }
    public abstract string VolumeChangeFailed { get; }
    public abstract string NotifyConfigTitle { get; }
    public abstract string CannotOpenSettings { get; }
    public abstract string NeedsAdmin { get; }
    public abstract string DataFolderInsecure(string folder);
    public abstract string Exit { get; }

    /// <summary>Nome na tela de uma tecla do atalho; <paramref name="id"/> é o id gravado no config.json.</summary>
    public abstract string KeyName(string id);

    // Core: validações sem acento achadas depois
    public abstract string VolumeStepRange { get; }
    public abstract string ShotChooseTarget { get; }
}
