using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GI_Subtitles;
using NAudio.SoundFont;
using Newtonsoft.Json;


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


    public static string FindClosestMatch(string input, Dictionary<string, string> voiceContentDict, out string Key)
    {
        string closestKey = null;
        int closestDistance = int.MaxValue;
        int length = input.Length;
        var keys = voiceContentDict.Keys.AsParallel();

        keys = keys.Where(key => !(length <= 5 && key.Length >= length * 3));

        keys.ForAll(key =>
        {

            string temp = key;
            if (length > 5 && temp.Length > length)
            {
                temp = temp.Substring(0, length);
            }

            // 如果input长度大于10且input在temp中，则直接返回完全匹配
            int distance;
            if (length > 10 && key.Contains(input))
            {
                distance = 0;
            }
            else
            {
                distance = CalculateLevenshteinDistance(input, temp);
            }
            lock (voiceContentDict)
            {
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestKey = key;
                }
            }
        });
        //Console.WriteLine($"closestKey {closestKey} length {length} closestDistance {closestDistance}");
        if (closestDistance < length / 1.5)
        {
            Key = closestKey;
            return voiceContentDict[closestKey];
        }
        else
        {
            Key = "";
            return "";
        }

    }

    private static int CalculateLevenshteinDistance(string a, string b)
    {

        if (string.IsNullOrEmpty(a)) return b.Length;
        if (string.IsNullOrEmpty(b)) return a.Length;

        int[] prev = new int[b.Length + 1];
        int[] curr = new int[b.Length + 1];

        for (int j = 0; j <= b.Length; j++)
            prev[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = (a[i - 1] == b[j - 1]) ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }

            var temp = prev;
            prev = curr;
            curr = temp;
        }
        return prev[b.Length];
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

    public static Dictionary<string, string> LoadAudioMap(string server, string game)
    {
        var data = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(server))
        {
            var serverPath = Path.Combine(game, "ServerMap.json");
            if (File.Exists(serverPath))
            {
                foreach (var pair in JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(serverPath)))
                {
                    data[pair.Key] = pair.Value;
                }
            }
        }
        else
        {
            foreach (var version in Directory.GetDirectories(game))
            {
                var jsonPath = Path.Combine(version, "AudioMap.json");
                if (File.Exists(jsonPath))
                {
                    foreach (var pair in JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(jsonPath)))
                    {
                        data[pair.Key] = pair.Value;
                    }
                }
            }
        }
        return data;
    }

}
