using System;
using System.Collections.Generic;

namespace GI_Subtitles.Core.Overlay
{
    public sealed class ActivityLogRow
    {
        private readonly List<OperatorJob> _jobs;

        public ActivityLogRow(
            DateTime utcTimestamp,
            OperatorJob job,
            int? pairOrdinal,
            string resultResourceKey,
            object[] resultFormatArguments)
            : this(
                utcTimestamp,
                new[] { job },
                pairOrdinal.HasValue ? ActivityLogScope.Pair : ActivityLogScope.Global,
                pairOrdinal,
                false,
                resultResourceKey,
                resultFormatArguments,
                null,
                null,
                null,
                false,
                false,
                false)
        {
        }

        public ActivityLogRow(
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
            _jobs = jobs == null || jobs.Count == 0
                ? new List<OperatorJob>()
                : new List<OperatorJob>(jobs);
            UtcTimestamp = utcTimestamp;
            Scope = scope;
            PairOrdinal = pairOrdinal;
            VoicePrimary = voicePrimary;
            ResultResourceKey = resultResourceKey;
            ResultFormatArguments = resultFormatArguments;
            OcrText = ocrText;
            Original = original;
            Translation = translation;
            DetectionMiss = detectionMiss;
            MatchMiss = matchMiss;
            IsRepeat = isRepeat;
        }

        public DateTime UtcTimestamp { get; }

        public OperatorJob Job
        {
            get { return _jobs.Count == 0 ? default(OperatorJob) : _jobs[0]; }
        }

        public IReadOnlyList<OperatorJob> Jobs
        {
            get { return _jobs; }
        }

        public ActivityLogScope Scope { get; }

        public int? PairOrdinal { get; }

        public bool VoicePrimary { get; }

        public string ResultResourceKey { get; }

        public object[] ResultFormatArguments { get; }

        public string OcrText { get; }

        public string Original { get; }

        public string Translation { get; }

        public bool DetectionMiss { get; }

        public bool MatchMiss { get; }

        public bool IsRepeat { get; }

        internal void IncludeVoiceJob()
        {
            if (!_jobs.Contains(OperatorJob.Voice))
            {
                _jobs.Add(OperatorJob.Voice);
            }
        }
    }
}
