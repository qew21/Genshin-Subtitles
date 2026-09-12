using System;
using System.Collections.Generic;

namespace GI_Subtitles.Core.Overlay
{
    /// <summary>
    /// Which pipeline output one result line carries. The match-miss line
    /// borrows <see cref="Original"/>; detection-miss and action lines carry
    /// no tag.
    /// </summary>
    public enum ActivityLogResultTag
    {
        None,
        Ocr,
        Original,
        Translation
    }

    /// <summary>
    /// One result line split at the tag boundary: the localized bracketed
    /// result tag and everything after it. Both parts come from their own
    /// resources, so a consumer never has to parse a localized prefix back
    /// out of the composed text.
    /// </summary>
    public sealed class ActivityLogResultLine
    {
        public ActivityLogResultLine(ActivityLogResultTag tag, string tagText, string contentText)
        {
            Tag = tag;
            TagText = tagText ?? string.Empty;
            ContentText = contentText ?? string.Empty;
        }

        public ActivityLogResultTag Tag { get; }

        public string TagText { get; }

        public string ContentText { get; }

        public string ToText()
        {
            return TagText + ContentText;
        }
    }

    /// <summary>
    /// The segmented projection of one row's result column. The segments are
    /// the single source for both layers the window renders: the colored
    /// TextBlock composes its runs from the lines, while the selection
    /// TextBox and the row-copy TSV read <see cref="PlainText"/>, so copied
    /// text always equals displayed text. Every compose call builds fresh
    /// line instances, which the window's bindings rely on to re-fire when a
    /// row is re-projected or a container is recycled.
    /// </summary>
    public sealed class ActivityLogResultProjection
    {
        public ActivityLogResultProjection(IReadOnlyList<ActivityLogResultLine> lines)
        {
            Lines = lines ?? (IReadOnlyList<ActivityLogResultLine>)new ActivityLogResultLine[0];
        }

        public IReadOnlyList<ActivityLogResultLine> Lines { get; }

        public string PlainText
        {
            get
            {
                var parts = new string[Lines.Count];
                for (int i = 0; i < Lines.Count; i++)
                {
                    parts[i] = Lines[i].ToText();
                }

                return string.Join(Environment.NewLine, parts);
            }
        }
    }

    /// <summary>
    /// Composes one row's result column as tag-plus-content segments. The
    /// resolver turns a resource key plus format arguments into localized
    /// text, keeping this class pure and testable without WPF resources.
    /// </summary>
    public static class ActivityLogResultComposer
    {
        public const string ResourceKeyTagOcr = "ActivityLog_Result_Tag_Ocr";
        public const string ResourceKeyTagOriginal = "ActivityLog_Result_Tag_Original";
        public const string ResourceKeyTagTranslation = "ActivityLog_Result_Tag_Translation";
        public const string ResourceKeyQuoted = "ActivityLog_Result_Quoted";
        public const string ResourceKeyMatchMiss = "ActivityLog_Result_MatchMiss";
        public const string ResourceKeyDetectionMiss = "ActivityLog_Result_DetectionMiss";

        public static ActivityLogResultProjection Compose(
            ActivityLogRow row,
            Func<string, object[], string> resolveText)
        {
            var lines = new List<ActivityLogResultLine>();
            if (row.DetectionMiss)
            {
                AddUntagged(lines, resolveText(ResourceKeyDetectionMiss, null));
            }
            else if (!string.IsNullOrEmpty(row.OcrText))
            {
                lines.Add(Tagged(
                    resolveText,
                    ActivityLogResultTag.Ocr,
                    ResourceKeyTagOcr,
                    ResourceKeyQuoted,
                    new object[] { row.OcrText }));
            }
            else
            {
                AddUntagged(lines, resolveText(row.ResultResourceKey, row.ResultFormatArguments));
            }

            if (row.MatchMiss)
            {
                lines.Add(Tagged(resolveText, ActivityLogResultTag.Original, ResourceKeyTagOriginal, ResourceKeyMatchMiss, null));
            }
            else
            {
                if (!string.IsNullOrEmpty(row.Original))
                {
                    lines.Add(Tagged(
                        resolveText,
                        ActivityLogResultTag.Original,
                        ResourceKeyTagOriginal,
                        ResourceKeyQuoted,
                        new object[] { row.Original }));
                }

                if (!string.IsNullOrEmpty(row.Translation))
                {
                    lines.Add(Tagged(
                        resolveText,
                        ActivityLogResultTag.Translation,
                        ResourceKeyTagTranslation,
                        ResourceKeyQuoted,
                        new object[] { row.Translation }));
                }
            }

            return new ActivityLogResultProjection(lines);
        }

        private static ActivityLogResultLine Tagged(
            Func<string, object[], string> resolveText,
            ActivityLogResultTag tag,
            string tagKey,
            string contentKey,
            object[] contentArguments)
        {
            return new ActivityLogResultLine(
                tag,
                resolveText(tagKey, null),
                resolveText(contentKey, contentArguments));
        }

        private static void AddUntagged(List<ActivityLogResultLine> lines, string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                lines.Add(new ActivityLogResultLine(ActivityLogResultTag.None, null, text));
            }
        }
    }
}
