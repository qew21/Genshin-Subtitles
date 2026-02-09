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
        // 1. Fast path: if there is an exact match, return it directly
        if (voiceContentDict.TryGetValue(input, out var exactMatch))
        {
            Key = input;
            return exactMatch;
        }

        int inputLen = input.Length;

        // Global best-result container (thread-safe updates)
        // Use an Object for the small amount of locking during final aggregation
        object globalLock = new object();
        string globalBestKey = null;
        int globalBestDistance = int.MaxValue;

        // 2. Parallel processing: use thread-local variables to avoid lock contention
        // This logic follows a Map-Reduce style:
        // localInit: each thread initializes its own best result
        // body: performs computation and updates the thread-local best result (lock-free)
        // localFinally: when a thread finishes, it merges its best result into the global result (locked, but rarely hit)
        Parallel.ForEach(
            voiceContentDict,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, // 用满所有核心
            () => new { BestKey = (string)null, BestDist = int.MaxValue }, // 线程局部变量初始化
            (kvp, loopState, localState) =>
            {
                string key = kvp.Key;
                int keyLen = key.Length;





                int currentDistance;

                // --- Contains logic (kept to preserve business behavior) ---
                bool isContains = false;
                if (inputLen > 10)
                {
                    // Use Ordinal comparison for best performance
                    if (key.IndexOf(input, StringComparison.Ordinal) >= 0 ||
                       (keyLen > 10 && input.IndexOf(key, StringComparison.Ordinal) >= 0))
                    {
                        isContains = true;
                    }
                }

                // --- Original filtering logic ---
                if (inputLen <= 5 && keyLen >= inputLen * 3)
                    return localState;
                // --- Pruning optimization ---
                // If the length difference is already greater than the current best distance in this thread, skip calculation
                // Note: this uses localState.BestDist, and pruning becomes more effective as the loop progresses
                if (Math.Abs(keyLen - inputLen) >= localState.BestDist)
                    return localState;
                if (isContains)
                {
                    currentDistance = 0;
                }
                else
                {
                    // --- Levenshtein calculation (zero allocation) ---
                    // Only calculate distance when not in the "contains" case
                    // To keep compatible with the original logic: if input > 5 and key is longer, take only the first inputLen chars of key
                    // Use Span to avoid allocations from Substring
                    ReadOnlySpan<char> targetSpan = key.AsSpan();
                    if (inputLen > 5 && keyLen > inputLen)
                    {
                        targetSpan = targetSpan.Slice(0, inputLen);
                    }

                    // Pass localState.BestDist as a threshold; stop early if the distance exceeds this value during calculation
                    currentDistance = CalculateLevenshteinDistance(input.AsSpan(), targetSpan, localState.BestDist);
                }

                // Update the best result in this thread
                if (currentDistance < localState.BestDist)
                {
                    return new { BestKey = key, BestDist = currentDistance };
                }

                return localState;
            },
            (finalLocalState) =>
            {
                // 3. Final merge: only this step needs a lock, and it runs at most once per thread (e.g. 8 or 16 times), so cost is minimal
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

        // Original threshold decision logic
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

    // Highly optimized Levenshtein algorithm
    // Features: uses Span, uses stackalloc, supports threshold-based early exit
    private static int CalculateLevenshteinDistance(ReadOnlySpan<char> source, ReadOnlySpan<char> target, int threshold)
    {
        int sourceLen = source.Length;
        int targetLen = target.Length;

        // If the length difference already exceeds the threshold, return immediately
        if (Math.Abs(sourceLen - targetLen) >= threshold) return threshold + 1;
        if (sourceLen == 0) return targetLen;
        if (targetLen == 0) return sourceLen;

        // Ensure source is the shorter string to reduce stack memory usage
        if (sourceLen > targetLen)
        {
            var temp = source; source = target; target = temp;
            var tempLen = sourceLen; sourceLen = targetLen; targetLen = tempLen;
        }

        // Allocate using stackalloc: extremely fast, does not trigger GC
        // Voice commands are usually short, so stackalloc is safe. If you are worried about overflow, you can add a length check and fall back to ArrayPool.
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

                    // Core state transition equation
                    int d1 = curr[i - 1] + 1;
                    int d2 = prev[i] + 1;
                    int d3 = prev[i - 1] + cost;

                    int dist = d1 < d2 ? d1 : d2;
                    dist = dist < d3 ? dist : d3;

                    curr[i] = dist;

                    if (dist < minDistanceInRow) minDistanceInRow = dist;
                }

                // Row-level pruning: if the minimum value in this row already exceeds the threshold,
                // then it is impossible for later matches to have a distance below the threshold; exit early
                if (minDistanceInRow >= threshold) return threshold + 1;

                // Swap buffers to avoid reinitializing arrays
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
