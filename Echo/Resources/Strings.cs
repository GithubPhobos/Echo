using System.Globalization;
using System.Resources;

namespace Echo.Resources;

/// <summary>
///     The texts of the interface in the current interface language. The texts themselves live in
///     'Strings.resx' and its translations; every property here must have an entry in each of those files.
/// </summary>
internal static class Strings
{
    private static readonly ResourceManager _resources = new("Echo.Resources.Strings", typeof(Strings).Assembly);

    /// <summary>
    ///     Returns the text for <paramref name="key"/> with its placeholders filled in.
    /// </summary>
    public static string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentUICulture, Get(key), arguments);

    /// <summary>
    ///     Returns the text for <paramref name="key"/>, falling back to English and then to the key itself.
    /// </summary>
    public static string Get(string key) =>
        _resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static string HintSelect => Get(nameof(HintSelect));

    public static string HintConfirm => Get(nameof(HintConfirm));

    public static string HintBack => Get(nameof(HintBack));

    public static string HintCancel => Get(nameof(HintCancel));

    public static string HintTryAgain => Get(nameof(HintTryAgain));

    public static string HintCheckAgain => Get(nameof(HintCheckAgain));

    public static string HintOpenFolder => Get(nameof(HintOpenFolder));

    public static string HintContinue => Get(nameof(HintContinue));

    public static string HintSkip => Get(nameof(HintSkip));

    public static string HintFinish => Get(nameof(HintFinish));

    public static string SetupTitle => Get(nameof(SetupTitle));

    public static string SetupReview => Get(nameof(SetupReview));

    public static string SetupStepOf => Get(nameof(SetupStepOf));

    public static string SetupNeedsTerminal => Get(nameof(SetupNeedsTerminal));

    public static string SetupSaved => Get(nameof(SetupSaved));

    public static string StepLanguages => Get(nameof(StepLanguages));

    public static string StepAcceleration => Get(nameof(StepAcceleration));

    public static string StepModel => Get(nameof(StepModel));

    public static string StepHotkey => Get(nameof(StepHotkey));

    public static string StepCheck => Get(nameof(StepCheck));

    public static string DownloadVerifying => Get(nameof(DownloadVerifying));

    public static string DownloadDetails => Get(nameof(DownloadDetails));

    public static string DownloadFailed => Get(nameof(DownloadFailed));

    public static string DiskSpaceLow => Get(nameof(DiskSpaceLow));

    public static string LanguageInterfaceQuestion => Get(nameof(LanguageInterfaceQuestion));

    public static string LanguageDictationQuestion => Get(nameof(LanguageDictationQuestion));

    public static string LanguageDictationNote => Get(nameof(LanguageDictationNote));

    public static string DictationAuto => Get(nameof(DictationAuto));

    public static string DictationAutoHint => Get(nameof(DictationAutoHint));

    public static string SummaryInterfaceLanguage => Get(nameof(SummaryInterfaceLanguage));

    public static string SummaryDictationLanguage => Get(nameof(SummaryDictationLanguage));

    public static string SummaryAcceleration => Get(nameof(SummaryAcceleration));

    public static string SummaryModel => Get(nameof(SummaryModel));

    public static string SummaryHotkey => Get(nameof(SummaryHotkey));

    public static string SummaryMicrophone => Get(nameof(SummaryMicrophone));

    public static string HardwareChecking => Get(nameof(HardwareChecking));

    public static string HardwareNoNvidia => Get(nameof(HardwareNoNvidia));

    public static string HardwareGpu => Get(nameof(HardwareGpu));

    public static string AccelerationQuestion => Get(nameof(AccelerationQuestion));

    public static string AccelerationAuto => Get(nameof(AccelerationAuto));

    public static string AccelerationAutoHint => Get(nameof(AccelerationAutoHint));

    public static string AccelerationCudaHint => Get(nameof(AccelerationCudaHint));

    public static string AccelerationVulkanHint => Get(nameof(AccelerationVulkanHint));

    public static string AccelerationCpu => Get(nameof(AccelerationCpu));

    public static string AccelerationCpuHint => Get(nameof(AccelerationCpuHint));

    public static string CudaGpuTooOld => Get(nameof(CudaGpuTooOld));

    public static string CudaDriverTooOld => Get(nameof(CudaDriverTooOld));

    public static string CudaLibrariesQuestion => Get(nameof(CudaLibrariesQuestion));

    public static string CudaDownload => Get(nameof(CudaDownload));

    public static string CudaDownloadHint => Get(nameof(CudaDownloadHint));

    public static string CudaAlreadyHave => Get(nameof(CudaAlreadyHave));

    public static string CudaAlreadyHaveHint => Get(nameof(CudaAlreadyHaveHint));

    public static string CudaDownloading => Get(nameof(CudaDownloading));

    public static string ModelQuestion => Get(nameof(ModelQuestion));

    public static string ModelRecommended => Get(nameof(ModelRecommended));

    public static string ModelDownloaded => Get(nameof(ModelDownloaded));

    public static string ModelAlreadyHave => Get(nameof(ModelAlreadyHave));

    public static string ModelAlreadyHaveHint => Get(nameof(ModelAlreadyHaveHint));

    public static string ModelTinyHint => Get(nameof(ModelTinyHint));

    public static string ModelBaseHint => Get(nameof(ModelBaseHint));

    public static string ModelSmallHint => Get(nameof(ModelSmallHint));

    public static string ModelMediumHint => Get(nameof(ModelMediumHint));

    public static string ModelTurboHint => Get(nameof(ModelTurboHint));

    public static string ModelTurboNoTranslateHint => Get(nameof(ModelTurboNoTranslateHint));

    public static string ModelLargeHint => Get(nameof(ModelLargeHint));

    public static string ModelDownloading => Get(nameof(ModelDownloading));

    public static string ModelCopyQuestion => Get(nameof(ModelCopyQuestion));

    public static string HotkeyQuestion => Get(nameof(HotkeyQuestion));

    public static string HotkeyCurrent => Get(nameof(HotkeyCurrent));

    public static string HotkeyChangeNote => Get(nameof(HotkeyChangeNote));

    public static string HotkeySuppressedNote => Get(nameof(HotkeySuppressedNote));

    public static string MicrophoneQuestion => Get(nameof(MicrophoneQuestion));

    public static string MicrophoneNone => Get(nameof(MicrophoneNone));

    public static string MicrophoneNoneHelp => Get(nameof(MicrophoneNoneHelp));

    public static string MicrophoneFailed => Get(nameof(MicrophoneFailed));

    public static string MicrophoneNoResponse => Get(nameof(MicrophoneNoResponse));

    public static string CalibrationQuiet => Get(nameof(CalibrationQuiet));

    public static string CalibrationSpeak => Get(nameof(CalibrationSpeak));

    public static string CalibrationNotHeard => Get(nameof(CalibrationNotHeard));

    public static string CalibrationNotHeardHelp => Get(nameof(CalibrationNotHeardHelp));

    public static string TestLoading => Get(nameof(TestLoading));

    public static string TestStartFailed => Get(nameof(TestStartFailed));

    public static string TestStartFailedHelp => Get(nameof(TestStartFailedHelp));

    public static string TestQuestion => Get(nameof(TestQuestion));

    public static string TestWaiting => Get(nameof(TestWaiting));

    public static string TestTranscribing => Get(nameof(TestTranscribing));

    public static string TestNothingHeard => Get(nameof(TestNothingHeard));

    public static string TestSeconds => Get(nameof(TestSeconds));

    public static string TestRestartNeeded => Get(nameof(TestRestartNeeded));

    public static string SetupRestartNeeded => Get(nameof(SetupRestartNeeded));

    public static string TestSlow => Get(nameof(TestSlow));

    public static string ReviewQuestion => Get(nameof(ReviewQuestion));

    public static string ReviewNote => Get(nameof(ReviewNote));

    public static string ReviewSave => Get(nameof(ReviewSave));

    public static string ReviewSaveHint => Get(nameof(ReviewSaveHint));

    public static string SubDictation => Get(nameof(SubDictation));

    public static string SubCudaLibraries => Get(nameof(SubCudaLibraries));

    public static string SubCalibration => Get(nameof(SubCalibration));

    public static string SubDictationTest => Get(nameof(SubDictationTest));

    public static string UnitMb => Get(nameof(UnitMb));

    public static string UnitGb => Get(nameof(UnitGb));

    public static string DictationAutoName => Get(nameof(DictationAutoName));

    public static string TagRecommended => Get(nameof(TagRecommended));

    public static string HardwareGpus => Get(nameof(HardwareGpus));

    public static string SubGraphicsCard => Get(nameof(SubGraphicsCard));

    public static string GraphicsCardQuestion => Get(nameof(GraphicsCardQuestion));

    public static string GraphicsCardHint => Get(nameof(GraphicsCardHint));

    public static string ModelOwnFileHint => Get(nameof(ModelOwnFileHint));

    public static string ModelPickFilesTitle => Get(nameof(ModelPickFilesTitle));

    public static string ModelNotAModel => Get(nameof(ModelNotAModel));

    public static string HotkeyNote => Get(nameof(HotkeyNote));

    public static string MicrophoneLevel => Get(nameof(MicrophoneLevel));

    public static string MicrophoneOpening => Get(nameof(MicrophoneOpening));


    public static string CudaSourceQuestion => Get(nameof(CudaSourceQuestion));

    public static string CudaPickFilesTitle => Get(nameof(CudaPickFilesTitle));

    public static string CudaNotALibrary => Get(nameof(CudaNotALibrary));

    public static string ModelRecommendationNote => Get(nameof(ModelRecommendationNote));



    public static string FileCopyFailed => Get(nameof(FileCopyFailed));

    public static string CudaAlreadyHaveSummary => Get(nameof(CudaAlreadyHaveSummary));

    public static string StepAdditional => Get(nameof(StepAdditional));

    public static string AdditionalQuestion => Get(nameof(AdditionalQuestion));

    public static string AdditionalNote => Get(nameof(AdditionalNote));

    public static string AdditionalTranslate => Get(nameof(AdditionalTranslate));

    public static string AdditionalTranslateHint => Get(nameof(AdditionalTranslateHint));

    public static string AdditionalTranslateWarning => Get(nameof(AdditionalTranslateWarning));

    public static string AdditionalAutoInsert => Get(nameof(AdditionalAutoInsert));

    public static string AdditionalAutoInsertHint => Get(nameof(AdditionalAutoInsertHint));

    public static string AdditionalContinue => Get(nameof(AdditionalContinue));

    public static string AdditionalContinueHint => Get(nameof(AdditionalContinueHint));

    public static string ValueOn => Get(nameof(ValueOn));

    public static string ValueOff => Get(nameof(ValueOff));

    public static string CalibrationQuestion => Get(nameof(CalibrationQuestion));

    public static string CalibrationWhy => Get(nameof(CalibrationWhy));

    public static string CalibrationHow => Get(nameof(CalibrationHow));

    public static string CalibrationCurrent => Get(nameof(CalibrationCurrent));

    public static string CalibrationDone => Get(nameof(CalibrationDone));

    public static string CalibrationDoneDetails => Get(nameof(CalibrationDoneDetails));

    public static string GroupRecording => Get(nameof(GroupRecording));

    public static string GroupGeneral => Get(nameof(GroupGeneral));

    public static string ValueAuto => Get(nameof(ValueAuto));

    public static string ValueNotSet => Get(nameof(ValueNotSet));

    public static string InputEmptyNote => Get(nameof(InputEmptyNote));

    public static string InputWholeError => Get(nameof(InputWholeError));

    public static string InputFractionError => Get(nameof(InputFractionError));

    public static string AdditionalPrompt => Get(nameof(AdditionalPrompt));

    public static string AdditionalPromptHint => Get(nameof(AdditionalPromptHint));

    public static string AdditionalCarryInitialPrompt => Get(nameof(AdditionalCarryInitialPrompt));

    public static string AdditionalCarryInitialPromptHint => Get(nameof(AdditionalCarryInitialPromptHint));

    public static string AdditionalNoContext => Get(nameof(AdditionalNoContext));

    public static string AdditionalNoContextHint => Get(nameof(AdditionalNoContextHint));

    public static string AdditionalSingleSegment => Get(nameof(AdditionalSingleSegment));

    public static string AdditionalSingleSegmentHint => Get(nameof(AdditionalSingleSegmentHint));

    public static string AdditionalSuppressBlank => Get(nameof(AdditionalSuppressBlank));

    public static string AdditionalSuppressBlankHint => Get(nameof(AdditionalSuppressBlankHint));

    public static string AdditionalTemperature => Get(nameof(AdditionalTemperature));

    public static string AdditionalTemperatureHint => Get(nameof(AdditionalTemperatureHint));

    public static string AdditionalThreads => Get(nameof(AdditionalThreads));

    public static string AdditionalThreadsHint => Get(nameof(AdditionalThreadsHint));

    public static string AdditionalSuppressRegex => Get(nameof(AdditionalSuppressRegex));

    public static string AdditionalSuppressRegexHint => Get(nameof(AdditionalSuppressRegexHint));

    public static string AdditionalBufferMs => Get(nameof(AdditionalBufferMs));

    public static string AdditionalBufferMsHint => Get(nameof(AdditionalBufferMsHint));

    public static string AdditionalNumberOfBuffers => Get(nameof(AdditionalNumberOfBuffers));

    public static string AdditionalNumberOfBuffersHint => Get(nameof(AdditionalNumberOfBuffersHint));

    public static string AdditionalMicrophoneLevelOutput => Get(nameof(AdditionalMicrophoneLevelOutput));

    public static string AdditionalMicrophoneLevelOutputHint => Get(nameof(AdditionalMicrophoneLevelOutputHint));

    public static string AdditionalPlaySound => Get(nameof(AdditionalPlaySound));

    public static string AdditionalPlaySoundHint => Get(nameof(AdditionalPlaySoundHint));

    public static string AdditionalVolume => Get(nameof(AdditionalVolume));

    public static string AdditionalVolumeHint => Get(nameof(AdditionalVolumeHint));

    public static string KeySpace => Get(nameof(KeySpace));

    public static string HintSwitch => Get(nameof(HintSwitch));

    public static string HintType => Get(nameof(HintType));

    public static string GroupRecognition => Get(nameof(GroupRecognition));

    public static string AdditionalTranslateCaution => Get(nameof(AdditionalTranslateCaution));

    public static string AdditionalLogLevel => Get(nameof(AdditionalLogLevel));

    public static string AdditionalLogLevelHint => Get(nameof(AdditionalLogLevelHint));

    public static string ListenStarting => Get(nameof(ListenStarting));

    public static string ListenListening => Get(nameof(ListenListening));

    public static string ListenRecording => Get(nameof(ListenRecording));

    public static string ListenTranscribing => Get(nameof(ListenTranscribing));

    public static string ListenPaused => Get(nameof(ListenPaused));

    public static string ListenFailed => Get(nameof(ListenFailed));


    public static string ListenScrolled => Get(nameof(ListenScrolled));

    public static string ListenPlaceholder => Get(nameof(ListenPlaceholder));

    public static string ListenTooSmall => Get(nameof(ListenTooSmall));

    public static string ListenHintRun => Get(nameof(ListenHintRun));

    public static string ListenHintLog => Get(nameof(ListenHintLog));

    public static string ListenHintQuit => Get(nameof(ListenHintQuit));

    public static string ListenSetupDone => Get(nameof(ListenSetupDone));

    public static string ListenFailedHint => Get(nameof(ListenFailedHint));

    public static string CommandSetup => Get(nameof(CommandSetup));

    public static string CommandPause => Get(nameof(CommandPause));

    public static string CommandResume => Get(nameof(CommandResume));

    public static string CommandLog => Get(nameof(CommandLog));

    public static string CommandLogArgument => Get(nameof(CommandLogArgument));

    public static string CommandHelp => Get(nameof(CommandHelp));

    public static string CommandQuit => Get(nameof(CommandQuit));

    public static string ResponseNotACommand => Get(nameof(ResponseNotACommand));

    public static string ResponseUnknownCommand => Get(nameof(ResponseUnknownCommand));

    public static string ResponsePaused => Get(nameof(ResponsePaused));

    public static string ResponseResumed => Get(nameof(ResponseResumed));

    public static string ResponseHelpTitle => Get(nameof(ResponseHelpTitle));

    public static string ResponseLogLevelNow => Get(nameof(ResponseLogLevelNow));

    public static string ResponseUnknownLevel => Get(nameof(ResponseUnknownLevel));

    public static string ResponseLogLevel => Get(nameof(ResponseLogLevel));

    public static string ResponseLogLevelNotSaved => Get(nameof(ResponseLogLevelNotSaved));

    public static string ListenHintPick => Get(nameof(ListenHintPick));

    public static string ListenHintClose => Get(nameof(ListenHintClose));

    public static string CommandInfo => Get(nameof(CommandInfo));

    public static string ResponseInfoTitle => Get(nameof(ResponseInfoTitle));

    public static string InfoSettingsFile => Get(nameof(InfoSettingsFile));

    public static string InfoLogsFolder => Get(nameof(InfoLogsFolder));

    public static string LevelVerbose => Get(nameof(LevelVerbose));

    public static string LevelDebug => Get(nameof(LevelDebug));

    public static string LevelInformation => Get(nameof(LevelInformation));

    public static string LevelWarning => Get(nameof(LevelWarning));

    public static string LevelError => Get(nameof(LevelError));

    public static string LevelFatal => Get(nameof(LevelFatal));

    public static string LevelCurrent => Get(nameof(LevelCurrent));

    public static string ProcessorUnsupported => Get(nameof(ProcessorUnsupported));

    public static string ProcessorUnsupportedHint => Get(nameof(ProcessorUnsupportedHint));

    public static string SubSystemCheck => Get(nameof(SubSystemCheck));

    public static string SystemCheckQuestion => Get(nameof(SystemCheckQuestion));

    public static string SystemCheckNote => Get(nameof(SystemCheckNote));

    public static string SystemChecking => Get(nameof(SystemChecking));

    public static string RequireDesktop => Get(nameof(RequireDesktop));

    public static string RequireDesktopWithout => Get(nameof(RequireDesktopWithout));

    public static string RequireSound => Get(nameof(RequireSound));

    public static string RequireSoundWithout => Get(nameof(RequireSoundWithout));

    public static string RequireMicrophone => Get(nameof(RequireMicrophone));

    public static string RequireMicrophoneWithout => Get(nameof(RequireMicrophoneWithout));

    public static string RequireMicrophoneFix => Get(nameof(RequireMicrophoneFix));

    public static string RequireClipboard => Get(nameof(RequireClipboard));

    public static string RequireClipboardWithout => Get(nameof(RequireClipboardWithout));

    public static string RequireShortcuts => Get(nameof(RequireShortcuts));

    public static string RequireShortcutsWithout => Get(nameof(RequireShortcutsWithout));

    public static string RequirePaste => Get(nameof(RequirePaste));

    public static string RequirePasteWithout => Get(nameof(RequirePasteWithout));

    public static string RequireFileChooser => Get(nameof(RequireFileChooser));

    public static string RequireFileChooserWithout => Get(nameof(RequireFileChooserWithout));

    public static string RequireUnpacker => Get(nameof(RequireUnpacker));

    public static string RequireUnpackerWithout => Get(nameof(RequireUnpackerWithout));

    public static string RequireInstall => Get(nameof(RequireInstall));

    public static string RequireInstallPackage => Get(nameof(RequireInstallPackage));

    public static string CudaToolsMissing => Get(nameof(CudaToolsMissing));

    public static string SubPastePermission => Get(nameof(SubPastePermission));

    public static string HotkeyDesktopQuestion => Get(nameof(HotkeyDesktopQuestion));

    public static string HotkeyDesktopNote => Get(nameof(HotkeyDesktopNote));

    public static string HintOpenDesktopWindow => Get(nameof(HintOpenDesktopWindow));

    public static string HintKeep => Get(nameof(HintKeep));

    public static string HotkeyDesktopWaiting => Get(nameof(HotkeyDesktopWaiting));

    public static string HotkeyDesktopRefused => Get(nameof(HotkeyDesktopRefused));

    public static string HotkeyDesktopRefusedNote => Get(nameof(HotkeyDesktopRefusedNote));

    public static string PastePermissionQuestion => Get(nameof(PastePermissionQuestion));

    public static string PastePermissionNote => Get(nameof(PastePermissionNote));

    public static string PastePermissionGranted => Get(nameof(PastePermissionGranted));

    public static string PastePermissionRefused => Get(nameof(PastePermissionRefused));

    public static string PastePermissionRefusedNote => Get(nameof(PastePermissionRefusedNote));

    public static string MicrophoneNoneHelpLinux => Get(nameof(MicrophoneNoneHelpLinux));

    public static string RequireRecognition => Get(nameof(RequireRecognition));

    public static string RequireRecognitionWithout => Get(nameof(RequireRecognitionWithout));

    public static string AccelerationVulkanTrial => Get(nameof(AccelerationVulkanTrial));

    public static string AccelerationVulkanTrialHint => Get(nameof(AccelerationVulkanTrialHint));

    public static string VulkanNoDriver => Get(nameof(VulkanNoDriver));

    public static string VulkanSoftwareOnly => Get(nameof(VulkanSoftwareOnly));

    public static string HintInstall => Get(nameof(HintInstall));

    public static string SystemCheckInstallNote => Get(nameof(SystemCheckInstallNote));

    public static string SystemInstalling => Get(nameof(SystemInstalling));

    public static string AdditionalSilenceThreshold => Get(nameof(AdditionalSilenceThreshold));

    public static string AdditionalSilenceThresholdHint => Get(nameof(AdditionalSilenceThresholdHint));

    public static string TagNotRecommended => Get(nameof(TagNotRecommended));

    public static string DesktopShortcutDescription => Get(nameof(DesktopShortcutDescription));

    public static string CopyFound => Get(nameof(CopyFound));

    public static string CopyIntoEcho => Get(nameof(CopyIntoEcho));

    public static string CopyDetails => Get(nameof(CopyDetails));

    public static string CopyDone => Get(nameof(CopyDone));

    public static string CommandCopy => Get(nameof(CommandCopy));

    public static string ResponseCopied => Get(nameof(ResponseCopied));

    public static string ResponseNothingToCopy => Get(nameof(ResponseNothingToCopy));

    public static string ResponseCopyFailed => Get(nameof(ResponseCopyFailed));

    public static string CalibrationMeasuring => Get(nameof(CalibrationMeasuring));

    public static string CalibrationMeasure => Get(nameof(CalibrationMeasure));

    public static string CalibrationManual => Get(nameof(CalibrationManual));

    public static string CalibrationManualHint => Get(nameof(CalibrationManualHint));



    public static string MicrophoneUnavailable => Get(nameof(MicrophoneUnavailable));

    public static string MicrophoneNotConnected => Get(nameof(MicrophoneNotConnected));

    public static string MicrophoneNotOpening => Get(nameof(MicrophoneNotOpening));

    public static string MicrophoneBack => Get(nameof(MicrophoneBack));

    public static string CommandUpdate => Get(nameof(CommandUpdate));

    public static string CommandRestart => Get(nameof(CommandRestart));

    public static string ListenHintUpdate => Get(nameof(ListenHintUpdate));

    public static string ResponseUpdateStarted => Get(nameof(ResponseUpdateStarted));

    public static string ResponseUpdateChecking => Get(nameof(ResponseUpdateChecking));

    public static string ResponseUpdateDownloading => Get(nameof(ResponseUpdateDownloading));

    public static string ResponseUpdateReady => Get(nameof(ResponseUpdateReady));

    public static string ResponseUpdateLatest => Get(nameof(ResponseUpdateLatest));

    public static string ResponseUpdateNotInstalled => Get(nameof(ResponseUpdateNotInstalled));

    public static string UpdateDownloaded => Get(nameof(UpdateDownloaded));

    public static string UpdateDownloadFailed => Get(nameof(UpdateDownloadFailed));

    public static string UpdateCheckFailed => Get(nameof(UpdateCheckFailed));

    public static string UpdateApplied => Get(nameof(UpdateApplied));

    public static string SummaryVersion => Get(nameof(SummaryVersion));

    public static string InfoVersionLatest => Get(nameof(InfoVersionLatest));

    public static string InfoVersionAvailable => Get(nameof(InfoVersionAvailable));

    public static string InfoVersionDownloading => Get(nameof(InfoVersionDownloading));

    public static string InfoVersionDownloaded => Get(nameof(InfoVersionDownloaded));

    public static string AdditionalAutoUpdate => Get(nameof(AdditionalAutoUpdate));

    public static string AdditionalAutoUpdateHint => Get(nameof(AdditionalAutoUpdateHint));

    public static string RestartFailed => Get(nameof(RestartFailed));

    public static string AccelerationRestartCaution => Get(nameof(AccelerationRestartCaution));

    public static string ReviewChangeQuestion => Get(nameof(ReviewChangeQuestion));

    public static string ReviewChangeSave => Get(nameof(ReviewChangeSave));

    public static string HintLeaveUnsaved => Get(nameof(HintLeaveUnsaved));

    public static string SetupUnchanged => Get(nameof(SetupUnchanged));
}