using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Remoting.Contexts;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Markup;

namespace GI_Subtitles
{
    public class SubtitleItem
    {
        public int Index { get; set; }
        public string TimeRange { get; set; }
        public List<string> Lines { get; set; } = new List<string>();
        public double StartTimeSeconds { get; set; }
        public double EndTimeSeconds { get; set; }

        public override string ToString()
        {
            return $"{Index}\r\n{TimeRange}\r\n{string.Join("\r\n", Lines)}\r\n";
        }
    }

    public class SrtProcessor
    {
        Dictionary<string, string> contentDict;
        public SrtProcessor(Dictionary<string, string> contentDict)
        {
            this.contentDict = contentDict;
        }
        // Read the SRT file and parse it into a SubtitleItem list
        public List<SubtitleItem> ReadSrtFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("SRT file not found", filePath);
            }

            var subtitles = new List<SubtitleItem>();
            var lines = File.ReadAllLines(filePath);
            SubtitleItem currentSubtitle = null;
            int lineNumber = 0;

            // SRT format regular expression pattern
            var timeRangePattern = @"^\d{2}:\d{2}:\d{2},\d{3} --> \d{2}:\d{2}:\d{2},\d{3}$";

            foreach (var line in lines)
            {
                lineNumber++;
                var trimmedLine = line.Trim();

                // Empty line means the current subtitle item ends
                if (string.IsNullOrEmpty(trimmedLine))
                {
                    if (currentSubtitle != null)
                    {
                        subtitles.Add(currentSubtitle);
                        currentSubtitle = null;
                    }
                    continue;
                }

                // If it is a new subtitle item and not yet initialized
                if (currentSubtitle == null)
                {
                    // Try to parse the index
                    if (int.TryParse(trimmedLine, out int index))
                    {
                        currentSubtitle = new SubtitleItem { Index = index };
                    }
                    else
                    {
                        throw new FormatException($"SRT format error, expected index on line {lineNumber} but found: {trimmedLine}");
                    }
                }
                // Check if it is a time range line
                else if (string.IsNullOrEmpty(currentSubtitle.TimeRange) &&
                         Regex.IsMatch(trimmedLine, timeRangePattern))
                {
                    currentSubtitle.TimeRange = trimmedLine;
                }
                // Otherwise add it to the subtitle lines
                else
                {
                    currentSubtitle.Lines.Add(trimmedLine);
                }
            }

            // Add the last subtitle item (if the file ends with an empty line)
            if (currentSubtitle != null)
            {
                subtitles.Add(currentSubtitle);
            }

            return subtitles;
        }

        // Write the processed subtitle list to the SRT file
        public void WriteSrtFile(string filePath, List<SubtitleItem> subtitles)
        {
            using (var writer = new StreamWriter(filePath, false))
            {
                foreach (var subtitle in subtitles)
                {
                    writer.WriteLine(subtitle.Index);
                    writer.WriteLine(subtitle.TimeRange);

                    foreach (var line in subtitle.Lines)
                    {
                        writer.WriteLine(line);
                    }

                    // Subtitle items are separated by empty lines
                    writer.WriteLine();
                }
            }
        }

        // Example method to convert subtitle content (can be modified as needed)
        public string ConvertSubtitleText(OptimizedMatcher Matcher, string text)
        {
            // Here is just an example: convert the text to uppercase
            string key;
            string res = Matcher.FindClosestMatch(text, out key);
            Logger.Log.Debug($"Convert {text} ocrResult: {res}");
            return res;
        }

        // Process the content of the entire subtitle list
        public List<SubtitleItem> ProcessSubtitles(OptimizedMatcher Matcher, List<SubtitleItem> subtitles)
        {
            var processedSubtitles = new List<SubtitleItem>();

            foreach (var subtitle in subtitles)
            {
                var processedSubtitle = new SubtitleItem
                {
                    Index = subtitle.Index,
                    TimeRange = subtitle.TimeRange
                };

                // Convert each line of subtitle content
                foreach (var line in subtitle.Lines)
                {
                    processedSubtitle.Lines.Add(ConvertSubtitleText(Matcher, line));
                }

                processedSubtitles.Add(processedSubtitle);
            }

            return processedSubtitles;
        }
    }
}
