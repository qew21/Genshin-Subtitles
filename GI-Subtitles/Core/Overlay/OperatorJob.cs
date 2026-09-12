namespace GI_Subtitles.Core.Overlay
{
    public enum OperatorJob
    {
        StartRecognition,
        StopRecognition,
        HideSubtitles,
        ShowSubtitles,
        BoxCapture,
        Refresh,
        VoiceSpeed,
        Preview,
        Capture,
        Ocr,
        Match,
        Voice,
        LanguagePackLoad,
        LanguagePackDownload
    }

    public enum ActivityLogScope
    {
        Global,
        Pair,
        DarkScreen,
        DialogueOptions
    }
}
