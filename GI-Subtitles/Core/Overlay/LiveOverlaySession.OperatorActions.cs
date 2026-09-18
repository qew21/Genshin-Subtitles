using System;
using System.Globalization;

namespace GI_Subtitles.Core.Overlay
{
    public sealed partial class LiveOverlaySession
    {
        public const int HintDurationMs = 2000;

        private const string HintResourceRecognitionRunning = "Hint_RecognitionRunning";
        private const string HintResourceRecognitionStopped = "Hint_RecognitionStopped";
        private const string HintResourceCaptureRegionBoxed = "Hint_CaptureRegionBoxed";
        private const string HintResourceSubtitlesHidden = "Hint_SubtitlesHidden";
        private const string HintResourceSubtitlesShown = "Hint_SubtitlesShown";
        private const string HintResourceRefreshed = "Hint_Refreshed";
        private const string HintResourceRefreshFoundNoText = "Hint_RefreshFoundNoText";
        private const string HintResourceVoiceSpeed = "Hint_VoiceSpeed";
        private const string HintResourceCaptureRegionMissing = "Hint_CaptureRegionMissing";

        private DateTime? _hintExpiresAt;

        public event EventHandler HintChanged;

        public bool HintVisible { get; private set; }

        public string HintResourceKey { get; private set; }

        public object[] HintFormatArguments { get; private set; }

        public void StartRecognition(bool hasCaptureRegion)
        {
            Tick();
            if (!hasCaptureRegion)
            {
                WriteOperatorAction(HintResourceCaptureRegionMissing);
                return;
            }

            RecognitionRunning = true;
            WriteOperatorAction(HintResourceRecognitionRunning);
        }

        public void StopRecognition()
        {
            Tick();
            RecognitionRunning = false;
            WriteOperatorAction(HintResourceRecognitionStopped);
        }

        public void HideSubtitles()
        {
            Tick();
            SubtitlesVisible = false;
            WriteOperatorAction(HintResourceSubtitlesHidden);
        }

        public void ShowSubtitles()
        {
            Tick();
            SubtitlesVisible = true;
            WriteOperatorAction(HintResourceSubtitlesShown);
        }

        public void CaptureRegionSelected()
        {
            CaptureRegionSelected(0);
        }

        public void CaptureRegionSelected(int pairId)
        {
            Tick();
            WriteOperatorAction(HintResourceCaptureRegionBoxed);
        }

        public void CaptureRegionSelectionCancelled()
        {
            Tick();
            WriteOperatorAction(HintResourceCaptureRegionMissing);
        }

        public void Refresh(bool hasCaptureRegion, bool foundText)
        {
            Tick();
            if (!hasCaptureRegion)
            {
                WriteOperatorAction(HintResourceCaptureRegionMissing);
                return;
            }

            if (foundText)
            {
                WriteOperatorAction(HintResourceRefreshed);
            }
            else
            {
                WriteOperatorAction(HintResourceRefreshFoundNoText);
            }
        }

        public void ChangeVoiceSpeed(double speed)
        {
            Tick();
            string speedText = speed.ToString("0.##", CultureInfo.InvariantCulture);
            WriteOperatorAction(HintResourceVoiceSpeed, speedText);
        }

        public void NoteOcrMiss()
        {
            Tick();
        }

        public void NoteMatchMiss()
        {
            Tick();
        }

        public void NoteVoicePlaybackStarted()
        {
        }

        public void NoteLanguagePackLoadStarted(string language)
        {
        }

        public void NoteLanguagePackLoadFinished(string language, bool succeeded)
        {
        }

        public void NoteLanguagePackDownloadStarted(string language)
        {
        }

        public void NoteLanguagePackDownloadFinished(string language, bool succeeded)
        {
        }

        private void ExpireHintIfNeeded()
        {
            if (HintVisible && _hintExpiresAt.HasValue && _utcNow() >= _hintExpiresAt.Value)
            {
                ClearHint();
            }
        }

        private void WriteOperatorAction(string resultResourceKey, params object[] formatArguments)
        {
            DateTime now = _utcNow();
            object[] args = formatArguments == null || formatArguments.Length == 0
                ? null
                : (object[])formatArguments.Clone();
            HintResourceKey = resultResourceKey;
            HintFormatArguments = args;
            HintVisible = true;
            _hintExpiresAt = now.AddMilliseconds(HintDurationMs);
            HintChanged?.Invoke(this, EventArgs.Empty);
        }

        private void WritePipelineForSlot(
            int slot,
            bool miss,
            string content,
            string ocrText,
            string original,
            bool matchMiss,
            bool isRepeat)
        {
        }

        private void WriteDialogueChoiceRow(string ocrText)
        {
        }

        private void RememberVoiceLogRow()
        {
        }

        private void ClearPendingVoiceLog()
        {
        }

        private void ClearHint()
        {
            HintVisible = false;
            HintResourceKey = null;
            HintFormatArguments = null;
            _hintExpiresAt = null;
            HintChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
