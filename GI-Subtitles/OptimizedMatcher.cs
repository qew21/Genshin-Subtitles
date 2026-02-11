using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace GI_Subtitles
{
    // Used to return the result of header and content separated
    public struct MatchResult
    {
        public string Header;
        public string Content;
    }

    public class OptimizedMatcher
    {
        // Store original data, use structure array to get the best memory layout
        private readonly Entry[] _entries;

        // Inverted index: Bigram (two-character fragment) -> list of all Entry IDs containing that fragment
        // For example: "狂猎" -> [5, 12, 108]
        private readonly Dictionary<string, List<int>> _bigramIndex;

        // Words with less than 2 characters cannot generate Bigram, store a separate bucket for fallback
        private readonly int[] _shortKeysIndices;
        private readonly Dictionary<string, string> ContentDict;
        public bool Loaded = false;
        public bool isEng = false;

        private struct Entry
        {
            public string Key;
            public string Value;
            public int Length;
        }

        public OptimizedMatcher(Dictionary<string, string> voiceContentDict, string inputLanguage)
        {
            isEng = inputLanguage == "EN";
            ContentDict = voiceContentDict;
            int count = voiceContentDict.Count;
            _entries = new Entry[count];
            _bigramIndex = new Dictionary<string, List<int>>(count * 3); // Estimated capacity
            var shortKeysList = new List<int>();

            int index = 0;
            foreach (var kvp in voiceContentDict)
            {
                string key = kvp.Key;
                if (!isEng)
                {
                    key = NormalizeInput(key);
                }

                // 1. Store the original data array
                _entries[index] = new Entry
                {
                    Key = key,
                    Value = kvp.Value,
                    Length = key.Length
                };

                // 2. Build the index (the core of memory for time)
                if (key.Length < 2)
                {
                    shortKeysList.Add(index);
                }
                else
                {
                    // Generate Bigrams (each two adjacent characters)
                    // "狂猎灾祸" -> "狂猎", "猎灾", "灾祸" 
                    // Use HashSet to deduplicate, avoid duplicate indices caused by duplicate fragments in the same word
                    var uniqueBigrams = new HashSet<string>();
                    for (int i = 0; i < key.Length - 1; i++)
                    {
                        // This is a pure memory operation, to sacrifice space for speed, directly Substring
                        // The extreme optimization can use int hash to replace string key, but it is easy to conflict, here is stable
                        string bigram = key.Substring(i, 2);
                        if (uniqueBigrams.Add(bigram))
                        {
                            if (!_bigramIndex.TryGetValue(bigram, out var list))
                            {
                                list = new List<int>();
                                _bigramIndex[bigram] = list;
                            }
                            list.Add(index);
                        }
                    }
                }
                index++;
            }
            _shortKeysIndices = shortKeysList.ToArray();
            Logger.Log.Info($"{voiceContentDict.Count} entries loaded.");
            Loaded = true;
        }

        public string FindClosestMatch(string input, out string Key)
        {
            if (!isEng)
            {
                input = NormalizeInput(input);
            }
            if (string.IsNullOrEmpty(input))
            {
                Key = "";
                return "";
            }

            int inputLen = input.Length;

            // --- Stage 1: Candidate Selection ---

            // Use HashSet to record IDs that need to be calculated precisely, automatically deduplicated
            // If input is very short (<2), Bigram index cannot be used, it can only fall back to scanning all short words
            HashSet<int> candidates;

            if (inputLen < 2)
            {
                // Input is too short, directly match all short words + possible indices
                // Here for simplicity, if the input is too short, we only scan the short word table
                candidates = new HashSet<int>(_shortKeysIndices);
            }
            else
            {
                // Input is long enough, use the index
                candidates = new HashSet<int>();

                // 1.1 Collect candidates
                // Strategy: as long as any Bigram in Input is hit, it is included in the candidates
                // Optimization: If Input is long enough, it can be required to hit at least 2 or 3 Bigrams 
                // But to avoid missing fuzzy matching, here we use the most relaxed strategy: hit 1 is enough.
                for (int i = 0; i < inputLen - 1; i++)
                {
                    string bigram = input.Substring(i, 2);
                    if (_bigramIndex.TryGetValue(bigram, out var ids))
                    {
                        foreach (var id in ids)
                        {
                            candidates.Add(id);
                        }
                    }
                }

                // 1.2 Very important:
                // If it is "contains logic" (Input is a substring of Key), Key must contain all Bigrams of Input, so it must be in candidates.
                // If it is "Key is a substring of Input", Key maybe very short, so we need to add _shortKeysIndices to the fallback.
                foreach (var id in _shortKeysIndices) candidates.Add(id);

                // If no candidates are found (completely unrelated), return empty
                if (candidates.Count == 0)
                {
                    Key = "";
                    return "";
                }
            }

            // If the candidate set is too large (for example, more than 2000), parallel processing; otherwise, direct single-thread processing is faster
            // At this time, candidates.Count usually only has several tens to hundreds, much less than the previous 10000
            var candidateList = candidates.ToArray();

            // --- Stage 2: Exact Calculation ---
            // Directly reuse the logic that has been optimized to the extreme, just the data source has changed

            // Global best solution
            int globalBestDistance = int.MaxValue;
            string globalBestKey = null;
            object globalLock = new object();

            // Only when the candidate quantity is large enough is parallel processing enabled, otherwise the context switching overhead is greater than the benefit
            // Experience threshold: 200
            if (candidateList.Length > 200)
            {
                Parallel.ForEach(candidateList, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, (id, state) =>
                {
                    ProcessCandidate(id, input, inputLen, ref globalBestDistance, ref globalBestKey, globalLock, state);
                });
            }
            else
            {
                foreach (var id in candidateList)
                {
                    // Simulate an empty LoopState
                    ProcessCandidate(id, input, inputLen, ref globalBestDistance, ref globalBestKey, globalLock, null);
                    if (globalBestDistance == 0) break;
                }
            }

            // Result verification
            if (globalBestDistance < inputLen / 1.5)
            {
                Key = globalBestKey;
                // Note: here we need to check the Value again based on Key, or directly store the Value in the Entry (it is already stored here)
                // To return quickly, we need to check the original dictionary based on Key, or we store the Value in the Entry
                // Here we store the Value in the Entry, directly take it, avoid checking the dictionary again
                // But globalBestKey is only a string, we need to find the Value back
                return _entries.First(e => e.Key == globalBestKey).Value;
                // Optimization: the First is O(N), this is not good.
                // Correction: we should record bestIndex
            }

            Key = "";
            return "";
        }

        // Core processing logic (extracted for reuse)
        private void ProcessCandidate(int id, string input, int inputLen, ref int globalBestDistance, ref string globalBestKey, object lockObj, ParallelLoopState loopState)
        {
            int CheckLength = isEng ? 40 : 10;
            // Quick check: if there is a perfect match
            if (globalBestDistance == 0)
            {
                loopState?.Stop();
                return;
            }

            // Directly access the array index, extremely fast (O(1))
            ref readonly var entry = ref _entries[id];
            string key = entry.Key;
            int keyLen = entry.Length;

            // Original business filtering logic
            if (inputLen <= CheckLength / 2 && keyLen >= inputLen * 3) return;

            int currentDistance = int.MaxValue;
            bool isSpecialMatch = false;

            // Contains priority logic
            if (inputLen > CheckLength)
            {
                if (key.IndexOf(input, StringComparison.Ordinal) >= 0 ||
                   (keyLen > CheckLength && keyLen > CheckLength && input.IndexOf(key, StringComparison.Ordinal) >= 0))
                {
                    isSpecialMatch = true;
                    currentDistance = 0;
                }
            }

            if (!isSpecialMatch)
            {
                // Pruning
                if (Math.Abs(keyLen - inputLen) >= globalBestDistance * 3) return;

                ReadOnlySpan<char> targetSpan = key.AsSpan();
                if (inputLen > CheckLength / 2 && keyLen > inputLen)
                {
                    targetSpan = targetSpan.Slice(0, inputLen);
                }

                currentDistance = CalculateLevenshteinDistance(input.AsSpan(), targetSpan, globalBestDistance);
            }

            if (currentDistance < globalBestDistance)
            {
                lock (lockObj)
                {
                    if (currentDistance < globalBestDistance)
                    {
                        globalBestDistance = currentDistance;
                        globalBestKey = key;
                        if (currentDistance == 0) loopState?.Stop();
                    }
                }
            }
        }

        // Keep the extreme optimization of Levenshtein unchanged
        private static int CalculateLevenshteinDistance(ReadOnlySpan<char> source, ReadOnlySpan<char> target, int threshold)
        {
            int sourceLen = source.Length;
            int targetLen = target.Length;

            if (Math.Abs(sourceLen - targetLen) >= threshold) return threshold + 1;
            if (sourceLen == 0) return targetLen;
            if (targetLen == 0) return sourceLen;

            if (sourceLen > targetLen)
            {
                var temp = source; source = target; target = temp;
                var tempLen = sourceLen; sourceLen = targetLen; targetLen = tempLen;
            }

            Span<int> prev = sourceLen < 512 ? stackalloc int[sourceLen + 1] : new int[sourceLen + 1];
            Span<int> curr = sourceLen < 512 ? stackalloc int[sourceLen + 1] : new int[sourceLen + 1];

            for (int i = 0; i <= sourceLen; i++) prev[i] = i;

            for (int j = 1; j <= targetLen; j++)
            {
                curr[0] = j;
                int minInRow = j;
                for (int i = 1; i <= sourceLen; i++)
                {
                    int cost = (source[i - 1] == target[j - 1]) ? 0 : 1;
                    int d1 = curr[i - 1] + 1;
                    int d2 = prev[i] + 1;
                    int d3 = prev[i - 1] + cost;
                    int dist = d1 < d2 ? d1 : d2;
                    dist = dist < d3 ? dist : d3;
                    curr[i] = dist;
                    if (dist < minInRow) minInRow = dist;
                }
                if (minInRow >= threshold) return threshold + 1;
                var tempRow = prev; prev = curr; curr = tempRow;
            }
            return prev[sourceLen];
        }

        // New method: return the result of header and content separated
        public MatchResult FindMatchWithHeaderSeparated(string ocrText, out string key)
        {
            key = "";
            var result = new MatchResult { Header = "", Content = "" };

            if (string.IsNullOrEmpty(ocrText))
                return result;

            string[] lines = ocrText.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 1)
            {
                result.Content = FindClosestMatch(lines[0], out key);
                return result;
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
                if (ContentDict.ContainsKey(header))
                {
                    if (!string.IsNullOrEmpty(headerMatch))
                        headerMatch += " ";
                    headerMatch += ContentDict[header];
                }
            }

            string bodyMatch = FindClosestMatch(bodyText, out string bodyKey);
            if (string.IsNullOrEmpty(bodyMatch))
            {
                result.Header = headerMatch;
                return result;
            }

            key = bodyKey;
            result.Content = bodyMatch;
            result.Header = headerMatch ?? "";
            return result;
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

        private static string NormalizeInput(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            // Remove all whitespace characters (space, newline, tab, etc.)
            return new string(input.Where(c => !char.IsWhiteSpace(c)).ToArray());
        }
    }
}