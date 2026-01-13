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
        // 读取SRT文件并解析为SubtitleItem列表
        public List<SubtitleItem> ReadSrtFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("SRT文件不存在", filePath);
            }

            var subtitles = new List<SubtitleItem>();
            var lines = File.ReadAllLines(filePath);
            SubtitleItem currentSubtitle = null;
            int lineNumber = 0;

            // SRT格式的正则表达式模式
            var timeRangePattern = @"^\d{2}:\d{2}:\d{2},\d{3} --> \d{2}:\d{2}:\d{2},\d{3}$";

            foreach (var line in lines)
            {
                lineNumber++;
                var trimmedLine = line.Trim();

                // 空行表示当前字幕项结束
                if (string.IsNullOrEmpty(trimmedLine))
                {
                    if (currentSubtitle != null)
                    {
                        subtitles.Add(currentSubtitle);
                        currentSubtitle = null;
                    }
                    continue;
                }

                // 如果是新的字幕项且尚未初始化
                if (currentSubtitle == null)
                {
                    // 尝试解析序号
                    if (int.TryParse(trimmedLine, out int index))
                    {
                        currentSubtitle = new SubtitleItem { Index = index };
                    }
                    else
                    {
                        throw new FormatException($"SRT格式错误，在第{lineNumber}行预期序号但找到: {trimmedLine}");
                    }
                }
                // 检查是否是时间范围行
                else if (string.IsNullOrEmpty(currentSubtitle.TimeRange) &&
                         Regex.IsMatch(trimmedLine, timeRangePattern))
                {
                    currentSubtitle.TimeRange = trimmedLine;
                }
                // 否则视为字幕内容行
                else
                {
                    currentSubtitle.Lines.Add(trimmedLine);
                }
            }

            // 添加最后一个字幕项（如果文件末尾没有空行）
            if (currentSubtitle != null)
            {
                subtitles.Add(currentSubtitle);
            }

            return subtitles;
        }

        // 将处理后的字幕列表写入SRT文件
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

                    // 字幕项之间用空行分隔
                    writer.WriteLine();
                }
            }
        }

        // 转换字幕内容的示例方法（可根据需要修改）
        public string ConvertSubtitleText(OptimizedMatcher Matcher, string text)
        {
            // 这里只是示例：将文本转为大写
            string key;
            string res = Matcher.FindClosestMatch(text, out key);
            Logger.Log.Debug($"Convert {text} ocrResult: {res}");
            return res;
        }

        // 处理整个字幕列表的内容
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

                // 转换每一行字幕内容
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
