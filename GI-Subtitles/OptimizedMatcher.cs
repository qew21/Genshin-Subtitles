using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GI_Subtitles
{
    public struct MatchResult
    {
        public string Header;
        public string Content;
    }

    public class OptimizedMatcher
    {
        private readonly Entry[] _entries;
        private readonly Dictionary<string, List<int>> _ngramIndex;
        private readonly int[] _shortKeysIndices;
        private readonly Dictionary<string, string> ContentDict;

        public bool Loaded = false;
        public bool isEng = false;

        private readonly int _ngramSize;

        private struct Entry
        {
            public string NormalizedKey;
            public string OriginalKey;
            public string Value;
            public int Length;
        }

        public OptimizedMatcher(Dictionary<string, string> voiceContentDict, string inputLanguage)
        {
            isEng = inputLanguage == "EN";
            ContentDict = voiceContentDict;

            // EN: 4-gram is crucial for performance (reduces candidates)
            // CN: 2-gram is sufficient
            _ngramSize = isEng ? 4 : 2;

            int count = voiceContentDict.Count;
            _entries = new Entry[count];
            _ngramIndex = new Dictionary<string, List<int>>(count * (isEng ? 4 : 2));
            var shortKeysList = new List<int>();

            int index = 0;
            foreach (var kvp in voiceContentDict)
            {
                string normKey = NormalizeInput(kvp.Key, isEng);

                _entries[index] = new Entry
                {
                    NormalizedKey = normKey,
                    OriginalKey = kvp.Key,
                    Value = kvp.Value,
                    Length = normKey.Length
                };

                if (normKey.Length < _ngramSize)
                {
                    shortKeysList.Add(index);
                }
                else
                {
                    var distinctGrams = new HashSet<string>();
                    for (int i = 0; i <= normKey.Length - _ngramSize; i++)
                    {
                        string gram = normKey.Substring(i, _ngramSize);
                        if (distinctGrams.Add(gram))
                        {
                            if (!_ngramIndex.TryGetValue(gram, out var list))
                            {
                                list = new List<int>();
                                _ngramIndex[gram] = list;
                            }
                            list.Add(index);
                        }
                    }
                }
                index++;
            }
            _shortKeysIndices = shortKeysList.ToArray();
            Loaded = true;
        }

        public string FindClosestMatch(string input, out string Key)
        {
            string normInput = NormalizeInput(input, isEng);

            if (string.IsNullOrEmpty(normInput))
            {
                Key = "";
                return "";
            }

            int inputLen = normInput.Length;
            // Use HashSet to avoid processing same ID twice
            HashSet<int> candidates = new HashSet<int>();

            // --- Stage 1: Candidate Selection (Smart Pruning) ---

            if (inputLen < _ngramSize)
            {
                foreach (var id in _shortKeysIndices) candidates.Add(id);
            }
            else
            {
                // PRUNING STRATEGY:
                // If a gram maps to > 1000 IDs, it's a "stop word" (e.g. "the", "ing").
                // We skip these to avoid flooding the candidate list.
                // We rely on "rare" grams (e.g. "Hanu", "synopsis") to find the specific entry.

                int maxCandidatesPerGram = 2000; // Safe threshold
                bool foundRareGram = false;

                // For very long inputs, we can step to save hash lookups, but safely.
                int step = inputLen > 50 ? 2 : 1;

                for (int i = 0; i <= inputLen - _ngramSize; i += step)
                {
                    string gram = normInput.Substring(i, _ngramSize);
                    if (_ngramIndex.TryGetValue(gram, out var ids))
                    {
                        // Pruning: Skip very common grams
                        if (ids.Count > maxCandidatesPerGram) continue;

                        foundRareGram = true;
                        foreach (var id in ids)
                        {
                            candidates.Add(id);
                        }
                    }
                }

                // Fallback: If ALL grams were "common" (very rare case), we must search everything found
                if (!foundRareGram)
                {
                    for (int i = 0; i <= inputLen - _ngramSize; i += step)
                    {
                        string gram = normInput.Substring(i, _ngramSize);
                        if (_ngramIndex.TryGetValue(gram, out var ids))
                        {
                            foreach (var id in ids) candidates.Add(id);
                        }
                    }
                }

                // Always check short keys if input is short-ish, but for long inputs they are irrelevant
                if (inputLen < 10)
                {
                    foreach (var id in _shortKeysIndices) candidates.Add(id);
                }
            }

            if (candidates.Count == 0)
            {
                Key = "";
                return "";
            }

            // --- Stage 2: Exact Calculation ---

            int globalBestDistance = int.MaxValue;
            int bestIndex = -1;

            foreach (int id in candidates)
            {
                ref readonly var entry = ref _entries[id];
                int keyLen = entry.Length;

                int currentDistance;

                // Optimized Logic for Subtitles (Prefix Matching)
                if (keyLen >= inputLen)
                {
                    // 1. FAST PATH: String StartsWith check
                    // If the Key starts EXACTLY with the Input (after normalization), distance is 0.
                    // This is O(1) compared to Levenshtein and covers 80% of perfect OCR cases.
                    if (entry.NormalizedKey.StartsWith(normInput, StringComparison.Ordinal))
                    {
                        currentDistance = 0;
                    }
                    else
                    {
                        // 2. Fallback: Levenshtein on Prefix
                        // Only compare the relevant slice.
                        // BUG FIX: Do NOT prune based on total length difference here.
                        ReadOnlySpan<char> keySpan = entry.NormalizedKey.AsSpan().Slice(0, inputLen);
                        currentDistance = CalculateLevenshteinDistance(normInput.AsSpan(), keySpan, globalBestDistance);
                    }
                }
                else
                {
                    // Key is shorter than Input (Reverse containment)
                    // Pruning is safe here: if length diff > best so far, skip
                    if ((inputLen - keyLen) > globalBestDistance) continue;

                    if (normInput.StartsWith(entry.NormalizedKey, StringComparison.Ordinal))
                    {
                        currentDistance = 0;
                    }
                    else
                    {
                        currentDistance = CalculateLevenshteinDistance(normInput.AsSpan(), entry.NormalizedKey.AsSpan(), globalBestDistance);
                    }
                }

                if (currentDistance < globalBestDistance)
                {
                    globalBestDistance = currentDistance;
                    bestIndex = id;
                    // Perfect match found, exit immediately
                    if (currentDistance == 0) break;
                }
            }

            // --- Stage 3: Verification ---

            if (bestIndex != -1)
            {
                // Dynamic Threshold: English needs more tolerance due to OCR noise
                double threshold = isEng ? Math.Max(5, inputLen * 0.4) : Math.Max(2, inputLen * 0.4);

                if (globalBestDistance <= threshold)
                {
                    Key = _entries[bestIndex].OriginalKey;
                    return _entries[bestIndex].Value;
                }
            }

            Key = "";
            return "";
        }

        private static int CalculateLevenshteinDistance(ReadOnlySpan<char> source, ReadOnlySpan<char> target, int threshold)
        {
            int sourceLen = source.Length;
            int targetLen = target.Length;

            // Basic checks
            if (sourceLen == 0) return targetLen;
            if (targetLen == 0) return sourceLen;
            if (Math.Abs(sourceLen - targetLen) > threshold) return threshold + 1;

            if (sourceLen > targetLen)
            {
                var temp = source; source = target; target = temp;
                var tempLen = sourceLen; sourceLen = targetLen; targetLen = tempLen;
            }

            // Stack allocation for speed (up to 512 chars)
            Span<int> prev = sourceLen < 512 ? stackalloc int[sourceLen + 1] : new int[sourceLen + 1];
            Span<int> curr = sourceLen < 512 ? stackalloc int[sourceLen + 1] : new int[sourceLen + 1];

            for (int i = 0; i <= sourceLen; i++) prev[i] = i;

            for (int j = 1; j <= targetLen; j++)
            {
                curr[0] = j;
                int minInRow = j;
                char targetChar = target[j - 1];

                for (int i = 1; i <= sourceLen; i++)
                {
                    int cost = (source[i - 1] == targetChar) ? 0 : 1;
                    int d1 = curr[i - 1] + 1;
                    int d2 = prev[i] + 1;
                    int d3 = prev[i - 1] + cost;

                    int dist = (d1 < d2) ? d1 : d2;
                    if (d3 < dist) dist = d3;

                    curr[i] = dist;
                    if (dist < minInRow) minInRow = dist;
                }

                if (minInRow > threshold) return threshold + 1;
                var tempRow = prev; prev = curr; curr = tempRow;
            }

            return prev[sourceLen];
        }

        private static string NormalizeInput(string input, bool isEng)
        {
            if (string.IsNullOrEmpty(input)) return "";

            if (!isEng)
            {
                // CN: Remove whitespace only
                int len = input.Length;
                char[] result = new char[len];
                int idx = 0;
                for (int i = 0; i < len; i++)
                {
                    if (!char.IsWhiteSpace(input[i])) result[idx++] = input[i];
                }
                return new string(result, 0, idx);
            }
            else
            {
                // EN: Aggressive Normalization (Letters & Digits only, Lowercase)
                // Removes punctuation logic which causes mismatches
                int len = input.Length;
                char[] result = new char[len];
                int idx = 0;
                for (int i = 0; i < len; i++)
                {
                    char c = input[i];
                    if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                    {
                        result[idx++] = c;
                    }
                    else if (c >= 'A' && c <= 'Z')
                    {
                        result[idx++] = (char)(c + 32);
                    }
                }
                return new string(result, 0, idx);
            }
        }

        // Header detection logic remains identical
        public MatchResult FindMatchWithHeaderSeparated(string ocrText, out string key)
        {
            key = "";
            var result = new MatchResult { Header = "", Content = "" };

            if (string.IsNullOrEmpty(ocrText)) return result;

            string[] lines = ocrText.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 1)
            {
                result.Content = FindClosestMatch(lines[0], out key);
                return result;
            }

            int maxLength = 0;
            int maxIndex = 0;
            if (!isEng)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Length > maxLength)
                    {
                        maxLength = lines[i].Length;
                        maxIndex = i;
                    }
                }
                if (IsTitleCase(lines[maxIndex]) && IsEnglishLine(lines[maxIndex]) && maxIndex < lines.Length - 1)
                {
                    maxIndex++;
                }
            }
            else
            {
                if (lines[1].Length > lines[0].Length * 2)
                {
                    maxIndex = 1;
                }
            }

            List<string> headers = new List<string>();
            for (int i = 0; i < maxIndex; i++) headers.Add(lines[i]);

            string bodyText = string.Join(" ", lines.Skip(maxIndex));

            string headerMatch = "";
            foreach (string header in headers)
            {
                if (ContentDict.ContainsKey(header) && !ContentDict[header].Contains("test"))
                {
                    if (!string.IsNullOrEmpty(headerMatch)) headerMatch += " ";
                    headerMatch += ContentDict[header];
                }
            }
            string bodyMatch = FindClosestMatch(bodyText, out string bodyKey);

            key = bodyKey;
            result.Content = bodyMatch;
            result.Header = headerMatch;
            return result;
        }

        private static bool IsTitleCase(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (char.IsLetter(text[0]) && !char.IsUpper(text[0])) return false;
            return true;
        }

        private static bool IsEnglishLine(string text)
        {
            int engCount = 0;
            int len = 0;
            foreach (char c in text)
            {
                if (char.IsLetter(c))
                {
                    len++;
                    if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) engCount++;
                }
            }
            return len > 0 && ((double)engCount / len) > 0.8;
        }
    }
}