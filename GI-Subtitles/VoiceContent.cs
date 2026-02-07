using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GI_Subtitles;
using NAudio.SoundFont;
using Newtonsoft.Json;
using System.Buffers;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;


public static class VoiceContentHelper
{
    public static Dictionary<string, string> CreateVoiceContentDictionary(string inputFilePath, string outputFilePath, string userName)
    {
        var jsonFilePath = Path.Combine(Path.GetDirectoryName(inputFilePath),
            $"{Path.GetFileNameWithoutExtension(inputFilePath)}_{Path.GetFileNameWithoutExtension(outputFilePath)}.json");
        if (File.Exists(jsonFilePath))
        {
            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(jsonFilePath));
            }
            catch (Exception ex)
            {
                Logger.Log.Error(ex);
            }
        }

        var chsData = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(inputFilePath));
        var enData = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(outputFilePath));
        var voiceContentDict = new Dictionary<string, string>();
        foreach (var chsItem in chsData)
        {
            if (enData.TryGetValue(chsItem.Key, out var enVoiceContent))
            {
                string pattern1 = @"\{.*?\}";
                string pattern2 = @"</?unbreak>";
                string temp = chsItem.Value;
                temp = Regex.Replace(temp, pattern1, "");
                temp = Regex.Replace(temp, @"<color=.*?>(.*?)</color>", "$1");
                enVoiceContent = ProcessGender(enVoiceContent);
                enVoiceContent = Regex.Replace(enVoiceContent, @"<color=.*?>(.*?)</color>", "$1");
                enVoiceContent = enVoiceContent.Replace("{NICKNAME}", userName).Replace("#", "");
                enVoiceContent = Regex.Replace(enVoiceContent, pattern1, "");
                temp = Regex.Replace(temp, pattern2, "").Replace("#", "").Replace("\\n", "");
                enVoiceContent = Regex.Replace(enVoiceContent, pattern2, "");
                voiceContentDict[temp] = enVoiceContent;
            }
        }
        var contentJson = JsonConvert.SerializeObject(voiceContentDict, Formatting.Indented);
        File.WriteAllText(jsonFilePath, contentJson);
        return voiceContentDict;
    }


    public static string FindMatchWithHeader(string ocrText, Dictionary<string, string> voiceContentDict, out string key)
    {
        key = "";

        if (string.IsNullOrEmpty(ocrText))
            return "";

        string[] lines = ocrText.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 1)
        {
            return FindClosestMatch(lines[0], voiceContentDict, out key);
        }

        // Find the longest line (body text starts from here)
        int maxLength = 0;
        int maxIndex = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length > maxLength)
            {
                maxLength = lines[i].Length;
                maxIndex = i;
            }
        }

        // Headers are lines above the longest line (1-2 lines)
        List<string> headers = new List<string>();
        // If the longest line is title case and English, and there's content after it, use the next line
        if (IsTitleCase(lines[maxIndex]) && IsEnglish(lines[maxIndex]) && maxIndex < lines.Length - 1)
        {
            maxIndex = maxIndex + 1;
        }


        for (int i = 0; i < maxIndex; i++)
        {
            headers.Add(lines[i]);
        }

        // Body text is the longest line and all lines after it
        string bodyText = string.Join(" ", lines.Skip(maxIndex));

        // Exact match for headers (no fuzzy matching)
        string headerMatch = "";
        foreach (string header in headers)
        {
            if (voiceContentDict.ContainsKey(header))
            {
                if (!string.IsNullOrEmpty(headerMatch))
                    headerMatch += " ";
                headerMatch += voiceContentDict[header];
            }
        }

        string bodyMatch = FindClosestMatch(bodyText, voiceContentDict, out string bodyKey);
        if (string.IsNullOrEmpty(bodyMatch))
        {
            return headerMatch;
        }

        key = bodyKey;
        if (!string.IsNullOrEmpty(headerMatch))
        {
            return headerMatch + " " + bodyMatch;
        }
        else
        {
            return bodyMatch;
        }
    }


    public static string FindClosestMatch(string input, Dictionary<string, string> voiceContentDict, out string Key)
    {
        // 1. 极速路径：完全匹配直接返回
        if (voiceContentDict.TryGetValue(input, out var exactMatch))
        {
            Key = input;
            return exactMatch;
        }

        int inputLen = input.Length;

        // 全局最优解容器（线程安全更新）
        // 使用 Object 用于最终合并时的极少量锁
        object globalLock = new object();
        string globalBestKey = null;
        int globalBestDistance = int.MaxValue;

        // 2. 并行处理：使用线程局部变量避免锁竞争
        // 这里的逻辑对应 Map-Reduce：
        // localInit: 每个线程初始化自己的最优解
        // body: 执行计算，更新线程内的最优解（无锁）
        // localFinally: 线程结束时，将自己的最优解合并到全局（有锁，但极少触发）
        Parallel.ForEach(
            voiceContentDict,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, // 用满所有核心
            () => new { BestKey = (string)null, BestDist = int.MaxValue }, // 线程局部变量初始化
            (kvp, loopState, localState) =>
            {
                string key = kvp.Key;
                int keyLen = key.Length;





                int currentDistance;

                // --- 包含逻辑 (保留业务需求) ---
                bool isContains = false;
                if (inputLen > 10)
                {
                    // 使用 Ordinal 比较，性能最高
                    if (key.IndexOf(input, StringComparison.Ordinal) >= 0 ||
                       (keyLen > 10 && input.IndexOf(key, StringComparison.Ordinal) >= 0))
                    {
                        isContains = true;
                    }
                }

                // --- 原始过滤逻辑 ---
                if (inputLen <= 5 && keyLen >= inputLen * 3)
                    return localState;
                // --- 剪枝优化 ---
                // 如果长度差已经大于该线程目前找到的最小距离，直接跳过计算
                // 注意：这里用 localState.BestDist，随着遍历进行，剪枝效率会越来越高
                if (Math.Abs(keyLen - inputLen) >= localState.BestDist)
                    return localState;
                if (isContains)
                {
                    currentDistance = 0;
                }
                else
                {
                    // --- Levenshtein 计算 (零内存分配) ---
                    // 只有当不包含时才计算距离
                    // 为了兼容原始逻辑：如果 input > 5 且 key 更长，截取 key 的前 inputLen 个字符
                    // 使用 Span 避免 Substring 的内存分配
                    ReadOnlySpan<char> targetSpan = key.AsSpan();
                    if (inputLen > 5 && keyLen > inputLen)
                    {
                        targetSpan = targetSpan.Slice(0, inputLen);
                    }

                    // 传入 localState.BestDist 作为阈值，如果计算过程中超过这个值立即停止
                    currentDistance = CalculateLevenshteinDistance(input.AsSpan(), targetSpan, localState.BestDist);
                }

                // 更新线程局部最优解
                if (currentDistance < localState.BestDist)
                {
                    return new { BestKey = key, BestDist = currentDistance };
                }

                return localState;
            },
            (finalLocalState) =>
            {
                // 3. 最终合并：只有这里需要锁，且次数等于线程数（例如只有 8 次或 16 次），几乎无开销
                if (finalLocalState.BestKey != null)
                {
                    lock (globalLock)
                    {
                        if (finalLocalState.BestDist < globalBestDistance)
                        {
                            globalBestDistance = finalLocalState.BestDist;
                            globalBestKey = finalLocalState.BestKey;
                        }
                    }
                }
            }
        );

        // Logger.Log.Debug($"closestKey {globalBestKey} length {inputLen} closestDistance {globalBestDistance}");

        // 原始阈值判定逻辑
        if (globalBestDistance < inputLen / 1.5)
        {
            Key = globalBestKey;
            return voiceContentDict[globalBestKey];
        }
        else
        {
            Key = "";
            return "";
        }
    }

    // 经过极致优化的 Levenshtein 算法
    // 特性：使用 Span，使用 stackalloc，支持阈值截断
    private static int CalculateLevenshteinDistance(ReadOnlySpan<char> source, ReadOnlySpan<char> target, int threshold)
    {
        int sourceLen = source.Length;
        int targetLen = target.Length;

        // 如果长度差已经超过阈值，直接返回
        if (Math.Abs(sourceLen - targetLen) >= threshold) return threshold + 1;
        if (sourceLen == 0) return targetLen;
        if (targetLen == 0) return sourceLen;

        // 确保 source 是较短的，减少栈内存占用
        if (sourceLen > targetLen)
        {
            var temp = source; source = target; target = temp;
            var tempLen = sourceLen; sourceLen = targetLen; targetLen = tempLen;
        }

        // Stackalloc 分配内存：极快，不触发 GC
        // 一般语音指令长度有限，使用 stackalloc 很安全。如果担心溢出，可以加长度判断回退到 ArrayPool
        Span<int> prev = stackalloc int[sourceLen + 1];
        Span<int> curr = stackalloc int[sourceLen + 1];

        for (int i = 0; i <= sourceLen; i++) prev[i] = i;

        for (int j = 1; j <= targetLen; j++)
        {
            curr[0] = j;
            int minDistanceInRow = j;

            for (int i = 1; i <= sourceLen; i++)
            {
                int cost = (source[i - 1] == target[j - 1]) ? 0 : 1;

                // 核心状态转移方程
                int d1 = curr[i - 1] + 1;
                int d2 = prev[i] + 1;
                int d3 = prev[i - 1] + cost;

                int dist = d1 < d2 ? d1 : d2;
                dist = dist < d3 ? dist : d3;

                curr[i] = dist;

                if (dist < minDistanceInRow) minDistanceInRow = dist;
            }

            // 行级剪枝：如果这一整行的最小值都已经超过了阈值，
            // 说明后续无论怎么匹配，距离都不可能小于阈值了，直接提前退出
            if (minDistanceInRow >= threshold) return threshold + 1;

            // 交换缓冲区，避免重新初始化数组
            var tempRow = prev;
            prev = curr;
            curr = tempRow;
        }

        return prev[sourceLen];
    }
    static string ProcessGender(string input)
    {
        string pattern = @"\{([FM]#.*?)}";
        Regex regex = new Regex(pattern);

        var matches = regex.Matches(input);
        if (matches.Count >= 1)
        {
            foreach (Match match in matches)
            {
                if (match.Groups[1].Value.StartsWith("F#"))
                {
                    string replacement = match.Groups[1].Value.Substring(2);
                    input = input.Replace(match.Value, replacement);
                }
                else
                {
                    input = input.Replace(match.Value, "");
                }
            }
        }

        return input;
    }

    public static string CalculateMd5Hash(string content, string SALE = "TIMWANG")
    {
        string combinedStr = content + SALE;
        byte[] inputBytes = Encoding.UTF8.GetBytes(combinedStr);
        using (MD5 md5Hash = MD5.Create())
        {
            byte[] hashBytes = md5Hash.ComputeHash(inputBytes);

            StringBuilder sb = new StringBuilder();
            foreach (byte b in hashBytes)
            {
                sb.Append(b.ToString("X2").ToLower());
            }
            return sb.ToString();
        }
    }

    private static bool IsTitleCase(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        string[] words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return false;

        foreach (string word in words)
        {
            if (word.Length > 0 && char.IsLetter(word[0]))
            {
                if (!char.IsUpper(word[0]))
                {
                    return false;
                }
            }
        }
        return true;
    }

    private static bool IsEnglish(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        foreach (char c in text)
        {
            if (char.IsLetter(c))
            {
                // Check if character is in English alphabet range (A-Z, a-z)
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                {
                    return true;
                }
            }
        }
        return false;
    }

}
