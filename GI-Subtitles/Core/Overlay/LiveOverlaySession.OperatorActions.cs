using System;
using System.Collections.Generic;
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

        private const string ResultResourceDetectionMiss = "ActivityLog_Result_DetectionMiss";
        private const string ResultResourceLanguagePackStart = "ActivityLog_Result_LanguagePackStart";
        private const string ResultResourceLanguagePackDone = "ActivityLog_Result_LanguagePackDone";
        private const string ResultResourceLanguagePackFailed = "ActivityLog_Result_LanguagePackFailed";

        private readonly List<ActivityLogRow> _activityLog = new List<ActivityLogRow>();
        private DateTime? _hintExpiresAt;
        private int _pendingVoiceLogIndex = -1;

        public event EventHandler HintChanged;

        public event EventHandler ActivityLogChanged;

        public IReadOnlyList<ActivityLogRow> ActivityLog
        {
            get { return _activityLog; }
        }

        public bool HintVisible { get; private set; }

        public string HintResourceKey { get; private set; }

        public object[] HintFormatArguments { get; private set; }

        public void StartRecognition(bool hasCaptureRegion)
        {
            Tick();
            if (!hasCaptureRegion)
            {
                WriteOperatorAction(OperatorJob.StartRecognition, null, HintResourceCaptureRegionMissing);
                return;
            }

            RecognitionRunning = true;
            WriteOperatorAction(OperatorJob.StartRecognition, null, HintResourceRecognitionRunning);
        }

        public void StopRecognition()
        {
            Tick();
            RecognitionRunning = false;
            WriteOperatorAction(OperatorJob.StopRecognition, null, HintResourceRecognitionStopped);
        }

        public void HideSubtitles()
        {
            Tick();
            SubtitlesVisible = false;
            WriteOperatorAction(OperatorJob.HideSubtitles, null, HintResourceSubtitlesHidden);
        }

        public void ShowSubtitles()
        {
            Tick();
            SubtitlesVisible = true;
            WriteOperatorAction(OperatorJob.ShowSubtitles, null, HintResourceSubtitlesShown);
        }

        public void CaptureRegionSelected()
        {
            CaptureRegionSelected(0);
        }

        public void CaptureRegionSelected(int pairId)
        {
            Tick();
            int index = IndexOfPair(pairId);
            int? ordinal = index >= 0 ? index + 1 : (int?)null;
            WriteOperatorAction(OperatorJob.BoxCapture, ordinal, HintResourceCaptureRegionBoxed);
        }

        public void CaptureRegionSelectionCancelled()
        {
            Tick();
            WriteOperatorAction(OperatorJob.BoxCapture, null, HintResourceCaptureRegionMissing);
        }

        public void Refresh(bool hasCaptureRegion, bool foundText)
        {
            Tick();
            if (!hasCaptureRegion)
            {
                WriteOperatorAction(OperatorJob.Refresh, null, HintResourceCaptureRegionMissing);
                return;
            }

            if (foundText)
            {
                WriteOperatorAction(OperatorJob.Refresh, null, HintResourceRefreshed);
            }
            else
            {
                WriteOperatorAction(OperatorJob.Refresh, null, HintResourceRefreshFoundNoText);
            }
        }

        public void ChangeVoiceSpeed(double speed)
        {
            Tick();
            string speedText = speed.ToString("0.##", CultureInfo.InvariantCulture);
            WriteOperatorAction(OperatorJob.VoiceSpeed, null, HintResourceVoiceSpeed, speedText);
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
            if (_pendingVoiceLogIndex >= 0 && _pendingVoiceLogIndex < _activityLog.Count)
            {
                _activityLog[_pendingVoiceLogIndex].IncludeVoiceJob();
                ActivityLogChanged?.Invoke(this, EventArgs.Empty);
            }

            _pendingVoiceLogIndex = -1;
        }

        public void NoteLanguagePackLoadStarted(string language)
        {
            WriteLanguagePackRow(OperatorJob.LanguagePackLoad, ResultResourceLanguagePackStart, language);
        }

        public void NoteLanguagePackLoadFinished(string language, bool succeeded)
        {
            WriteLanguagePackRow(
                OperatorJob.LanguagePackLoad,
                succeeded ? ResultResourceLanguagePackDone : ResultResourceLanguagePackFailed,
                language);
        }

        public void NoteLanguagePackDownloadStarted(string language)
        {
            WriteLanguagePackRow(OperatorJob.LanguagePackDownload, ResultResourceLanguagePackStart, language);
        }

        public void NoteLanguagePackDownloadFinished(string language, bool succeeded)
        {
            WriteLanguagePackRow(
                OperatorJob.LanguagePackDownload,
                succeeded ? ResultResourceLanguagePackDone : ResultResourceLanguagePackFailed,
                language);
        }

        private void ExpireHintIfNeeded()
        {
            if (HintVisible && _hintExpiresAt.HasValue && _utcNow() >= _hintExpiresAt.Value)
            {
                ClearHint();
            }
        }

        private void WriteOperatorAction(
            OperatorJob job,
            int? pairOrdinal,
            string resultResourceKey,
            params object[] formatArguments)
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

            bool voicePrimary = false;
            if (pairOrdinal.HasValue)
            {
                int index = pairOrdinal.Value - 1;
                if (index >= 0 && index < _pairs.Count)
                {
                    voicePrimary = _pairs[index].Id == VoicePrimaryId;
                }
            }

            AppendActivityLogRow(
                now,
                new[] { job },
                pairOrdinal.HasValue ? ActivityLogScope.Pair : ActivityLogScope.Global,
                pairOrdinal,
                voicePrimary,
                resultResourceKey,
                args,
                null,
                null,
                null,
                false,
                false,
                false);
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
            ActivityLogScope scope;
            int? pairOrdinal = null;
            bool voicePrimary = false;
            if (slot == DarkScreenOcrSlot)
            {
                scope = ActivityLogScope.DarkScreen;
            }
            else if (slot == DialogueOptionsOcrSlot)
            {
                scope = ActivityLogScope.DialogueOptions;
            }
            else
            {
                scope = ActivityLogScope.Pair;
                pairOrdinal = slot + 1;
                if (slot >= 0 && slot < _pairs.Count)
                {
                    voicePrimary = _pairs[slot].Id == VoicePrimaryId;
                }
            }

            WritePipelineResult(
                scope,
                pairOrdinal,
                voicePrimary,
                miss,
                content,
                ocrText,
                original,
                matchMiss,
                isRepeat);
        }

        private void WritePipelineResult(
            ActivityLogScope scope,
            int? pairOrdinal,
            bool voicePrimary,
            bool miss,
            string content,
            string ocrText,
            string original,
            bool matchMiss,
            bool isRepeat)
        {
            var jobs = new List<OperatorJob> { OperatorJob.Capture, OperatorJob.Ocr };
            string translation = null;
            string resultKey = null;
            bool detectionMiss = miss;
            if (detectionMiss)
            {
                resultKey = ResultResourceDetectionMiss;
                matchMiss = false;
                ocrText = null;
                original = null;
            }
            else
            {
                translation = string.IsNullOrEmpty(content) ? null : content;
                if (matchMiss || !string.IsNullOrEmpty(original) || !string.IsNullOrEmpty(translation))
                {
                    jobs.Add(OperatorJob.Match);
                }
                else
                {
                    matchMiss = false;
                }
            }

            AppendActivityLogRow(
                _utcNow(),
                jobs,
                scope,
                pairOrdinal,
                voicePrimary,
                resultKey,
                null,
                ocrText,
                original,
                translation,
                detectionMiss,
                matchMiss,
                isRepeat);
        }

        private void WriteDialogueChoiceRow(string ocrText)
        {
            AppendActivityLogRow(
                _utcNow(),
                new[] { OperatorJob.Match },
                ActivityLogScope.DialogueOptions,
                null,
                false,
                null,
                null,
                ocrText,
                null,
                null,
                false,
                false,
                false);
        }

        private void WriteLanguagePackRow(OperatorJob job, string resultResourceKey, string language)
        {
            AppendActivityLogRow(
                _utcNow(),
                new[] { job },
                ActivityLogScope.Global,
                null,
                false,
                resultResourceKey,
                new object[] { language ?? string.Empty },
                null,
                null,
                null,
                false,
                false,
                false);
        }

        private void AppendActivityLogRow(
            DateTime utcTimestamp,
            IReadOnlyList<OperatorJob> jobs,
            ActivityLogScope scope,
            int? pairOrdinal,
            bool voicePrimary,
            string resultResourceKey,
            object[] resultFormatArguments,
            string ocrText,
            string original,
            string translation,
            bool detectionMiss,
            bool matchMiss,
            bool isRepeat)
        {
            _activityLog.Add(new ActivityLogRow(
                utcTimestamp,
                jobs,
                scope,
                pairOrdinal,
                voicePrimary,
                resultResourceKey,
                resultFormatArguments,
                ocrText,
                original,
                translation,
                detectionMiss,
                matchMiss,
                isRepeat));
            ActivityLogChanged?.Invoke(this, EventArgs.Empty);
        }

        private void RememberVoiceLogRow()
        {
            _pendingVoiceLogIndex = _activityLog.Count - 1;
        }

        private void ClearPendingVoiceLog()
        {
            _pendingVoiceLogIndex = -1;
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
