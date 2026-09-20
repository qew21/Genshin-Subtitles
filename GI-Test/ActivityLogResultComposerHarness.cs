using System;
using System.Collections.Generic;
using GI_Subtitles.Core.Overlay;

namespace GI_Test
{
    /// <summary>
    /// Shared fixture for the result-column tests: mirrors the zh-CN
    /// resource values, stands in for the window's resource lookup, and
    /// builds the rows, so the composer and line-TextBox tests compose the
    /// same rows the window would.
    /// </summary>
    internal static class ActivityLogResultComposerHarness
    {
        internal static readonly Dictionary<string, string> Texts = new Dictionary<string, string>
        {
            { ActivityLogResultComposer.ResourceKeyTagOcr, "[OCR]" },
            { ActivityLogResultComposer.ResourceKeyTagOriginal, "[原文]" },
            { ActivityLogResultComposer.ResourceKeyTagTranslation, "[译文]" },
            { ActivityLogResultComposer.ResourceKeyQuoted, "「{0}」" },
            { ActivityLogResultComposer.ResourceKeyMatchMiss, " 匹配 miss" },
            { ActivityLogResultComposer.ResourceKeyDetectionMiss, "检测 miss" },
            { "Test_ActionFinished", "完成 {0}" }
        };

        internal static ActivityLogResultProjection Compose(ActivityLogRow row)
        {
            return ActivityLogResultComposer.Compose(row, Resolve);
        }

        internal static string Resolve(string key, object[] args)
        {
            string format;
            if (string.IsNullOrEmpty(key) || !Texts.TryGetValue(key, out format))
            {
                return string.Empty;
            }

            if (args == null || args.Length == 0)
            {
                return format;
            }

            return string.Format(format, args);
        }

        internal static ActivityLogRow Row(
            string ocrText = null,
            string original = null,
            string translation = null,
            bool detectionMiss = false,
            bool matchMiss = false,
            string resultResourceKey = null,
            object[] resultFormatArguments = null)
        {
            return new ActivityLogRow(
                new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc),
                new[] { OperatorJob.Capture, OperatorJob.Ocr, OperatorJob.Match },
                ActivityLogScope.Pair,
                1,
                true,
                resultResourceKey,
                resultFormatArguments,
                ocrText,
                original,
                translation,
                detectionMiss,
                matchMiss,
                false);
        }
    }
}
