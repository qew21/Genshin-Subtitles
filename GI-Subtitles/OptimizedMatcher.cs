using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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
        // Index mapping: N-Gram fragment -> List of Entry IDs
        private readonly Dictionary<string, List<int>> _ngramIndex;
        // Short keys bucket
        private readonly int[] _shortKeysIndices;
        private readonly Dictionary<string, string> ContentDict;

        public bool Loaded = false;
        public bool isEng = false;

        private readonly int _ngramSize;

        private struct Entry
        {
            public string NormalizedKey; // Compressed key for search (e.g., "lookthetitle")
            public string OriginalKey;   // Original key for logic (e.g., "...Look! The title")
            public string Value;         // Output value
            public int Length;           // Length of NormalizedKey
        }

        public OptimizedMatcher(Dictionary<string, string> voiceContentDict, string inputLanguage)
        {
            isEng = inputLanguage == "EN";
            ContentDict = voiceContentDict;

            // CN: 2-gram is optimal.
            // EN: Since we strip spaces/punctuation, text is dense. 4-gram filters best.
            _ngramSize = isEng ? 4 : 2;

            int count = voiceContentDict.Count;
            _entries = new Entry[count];
            _ngramIndex = new Dictionary<string, List<int>>(count * (isEng ? 4 : 2)); // Pre-allocate rough size
            var shortKeysList = new List<int>();

            int index = 0;
            foreach (var kvp in voiceContentDict)
            {
                // Prepare Key: Aggressive normalization
                string normKey = NormalizeInput(kvp.Key, isEng);

                _entries[index] = new Entry
                {
                    NormalizedKey = normKey,
                    OriginalKey = kvp.Key,
                    Value = kvp.Value,
                    Length = normKey.Length
                };

                // Build Index
                if (normKey.Length < _ngramSize)
                {
                    shortKeysList.Add(index);
                }
                else
                {
                    // Using a HashSet for the current key to prevent adding the same index multiple times for repeated grams
                    var distinctGrams = new HashSet<string>();
                    for (int i = 0; i <= normKey.Length - _ngramSize; i++)
                    {
                        // String allocation here is unavoidable for Dictionary Key, but happens only once at load
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
            // 1. Normalize Input (Same logic as Indexing)
            string normInput = NormalizeInput(input, isEng);

            if (string.IsNullOrEmpty(normInput))
            {
                Key = "";
                return "";
            }

            int inputLen = normInput.Length;

            // Reusing a list from a pool would be better, but local var is fine for now
            // We use HashSet to deduplicate candidate IDs
            HashSet<int> candidates = new HashSet<int>();

            // --- Stage 1: Candidate Selection ---

            if (inputLen < _ngramSize)
            {
                // Input is tiny, check all short keys
                foreach (var id in _shortKeysIndices) candidates.Add(id);
            }
            else
            {
                // Optimization: Don't check EVERY gram for long inputs, it floods candidates.
                // For Chinese, check every gram (high entropy).
                // For English, we can skip a bit if input is huge, but safe approach is check all.
                int step = 1;
                // If input is massive (>50 chars), we can step by 2 to save time looking up common grams, 
                // relying on the fact that a match will share many grams.
                if (inputLen > 50) step = 2;

                bool foundAny = false;
                for (int i = 0; i <= inputLen - _ngramSize; i += step)
                {
                    string gram = normInput.Substring(i, _ngramSize);
                    if (_ngramIndex.TryGetValue(gram, out var ids))
                    {
                        foundAny = true;
                        foreach (var id in ids)
                        {
                            candidates.Add(id);
                        }
                    }
                }

                // If no N-Grams match, fallback to short keys (extremely rare)
                if (!foundAny)
                {
                    foreach (var id in _shortKeysIndices) candidates.Add(id);
                }
            }

            if (candidates.Count == 0)
            {
                Key = "";
                return "";
            }

            // --- Stage 2: Exact Calculation (Single Threaded) ---

            int globalBestDistance = int.MaxValue;
            int bestIndex = -1;

            // Convert to Span for loop to avoid iterator allocation
            // Note: HashSet doesn't support Span directly, using foreach is fine.

            foreach (int id in candidates)
            {
                ref readonly var entry = ref _entries[id];
                int keyLen = entry.Length;

                // --- Logic: Subtitle Prefix Matching ---
                // Case A: Input is short ("Look!"), Key is long ("Look! The title is...") -> Match Prefix
                // Case B: Input is similar length to Key -> Match Full
                // Case C: Input is longer than Key -> Key must be substring of Input

                int currentDistance;

                // Heuristic: If lengths differ wildly, only proceed if one contains the other logic applies
                // But specifically for OCR subtitles: Dictionary is usually the "Full Text", Input is "Fragment".

                if (keyLen > inputLen)
                {
                    // Dictionary is longer. 
                    // Optimization: Only compare the first 'inputLen' characters of the Key.
                    // This simulates "Starts With" fuzzy matching.

                    // 1. Quick check: specific big difference threshold
                    if (keyLen > inputLen * 4 && !isEng) continue; // Skip huge mismatches in CN

                    // 2. Calculate distance against the PREFIX of the key
                    // This fixes the "Long Dictionary Entry" vs "Short OCR Line" problem
                    ReadOnlySpan<char> keySpan = entry.NormalizedKey.AsSpan().Slice(0, inputLen);
                    currentDistance = CalculateLevenshteinDistance(normInput.AsSpan(), keySpan, globalBestDistance);
                }
                else
                {
                    // Dictionary is shorter or equal. Standard match.
                    // Pruning: If length difference is already larger than best distance, skip.
                    if ((inputLen - keyLen) >= globalBestDistance) continue;

                    currentDistance = CalculateLevenshteinDistance(normInput.AsSpan(), entry.NormalizedKey.AsSpan(), globalBestDistance);
                }

                if (currentDistance < globalBestDistance)
                {
                    globalBestDistance = currentDistance;
                    bestIndex = id;
                    // Perfect match found
                    if (currentDistance == 0) break;
                }
            }

            // --- Stage 3: Verification ---

            if (bestIndex != -1)
            {
                // Dynamic Threshold
                // EN needs looser threshold (OCR adds garbage)
                // CN needs tighter threshold
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

        // Extremely optimized Levenshtein (Allocation free)
        private static int CalculateLevenshteinDistance(ReadOnlySpan<char> source, ReadOnlySpan<char> target, int threshold)
        {
            int sourceLen = source.Length;
            int targetLen = target.Length;

            // Direct cleanup checks
            if (sourceLen == 0) return targetLen;
            if (targetLen == 0) return sourceLen;

            // If length diff is already > threshold, don't bother
            if (Math.Abs(sourceLen - targetLen) > threshold) return threshold + 1;

            // Swap to ensure we use less memory for the row
            if (sourceLen > targetLen)
            {
                var temp = source; source = target; target = temp;
                var tempLen = sourceLen; sourceLen = targetLen; targetLen = tempLen;
            }

            // Stack allocation for small strings (fast), heap for huge ones
            // 256 chars is enough for most subtitle lines
            Span<int> prev = sourceLen < 256 ? stackalloc int[sourceLen + 1] : new int[sourceLen + 1];
            Span<int> curr = sourceLen < 256 ? stackalloc int[sourceLen + 1] : new int[sourceLen + 1];

            for (int i = 0; i <= sourceLen; i++) prev[i] = i;

            for (int j = 1; j <= targetLen; j++)
            {
                curr[0] = j;
                int minInRow = j;

                // Unrolling loop slightly or just simple access
                char targetChar = target[j - 1];

                for (int i = 1; i <= sourceLen; i++)
                {
                    int cost = (source[i - 1] == targetChar) ? 0 : 1;

                    int d1 = curr[i - 1] + 1; // deletion
                    int d2 = prev[i] + 1;     // insertion
                    int d3 = prev[i - 1] + cost; // substitution

                    // Min(d1, d2, d3)
                    int dist = (d1 < d2) ? d1 : d2;
                    if (d3 < dist) dist = d3;

                    curr[i] = dist;
                    if (dist < minInRow) minInRow = dist;
                }

                // Optimization: If the entire row is above threshold, stop early
                if (minInRow > threshold) return threshold + 1;

                // Swap rows
                var tempRow = prev; prev = curr; curr = tempRow;
            }

            return prev[sourceLen];
        }

        private static string NormalizeInput(string input, bool isEng)
        {
            if (string.IsNullOrEmpty(input)) return "";

            if (!isEng)
            {
                // CN: Original logic - simply remove whitespace.
                // This preserves punctuation which might be useful in CN, but mainly it's about removing spaces.
                // Using a fast char array construction.
                int len = input.Length;
                char[] result = new char[len];
                int idx = 0;
                for (int i = 0; i < len; i++)
                {
                    if (!char.IsWhiteSpace(input[i]))
                    {
                        result[idx++] = input[i];
                    }
                }
                return new string(result, 0, idx);
            }
            else
            {
                // EN: Aggressive Normalization. 
                // Remove ALL punctuation, spaces, and symbols. Keep only Letters and Digits.
                // "..Look! The" -> "lookthe"
                // This makes "shrunken" and "shrunken..." match perfectly in Prefix checks.
                int len = input.Length;
                char[] result = new char[len];
                int idx = 0;
                for (int i = 0; i < len; i++)
                {
                    char c = input[i];
                    if (char.IsLetterOrDigit(c))
                    {
                        // ToLower inline
                        if (c >= 'A' && c <= 'Z')
                        {
                            result[idx++] = (char)(c + 32);
                        }
                        else
                        {
                            result[idx++] = c;
                        }
                    }
                }
                return new string(result, 0, idx);
            }
        }

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
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Length > maxLength)
                {
                    maxLength = lines[i].Length;
                    maxIndex = i;
                }
            }

            // Simple header detection logic
            if (maxIndex > 0 && maxIndex < lines.Length)
            {
                // If the longest line is found, assume lines before it are headers
                // This is a simple heuristic from your original code
            }
            else if (IsTitleCase(lines[maxIndex]) && IsEnglish(lines[maxIndex]) && maxIndex < lines.Length - 1)
            {
                maxIndex++;
            }

            List<string> headers = new List<string>();
            for (int i = 0; i < maxIndex; i++) headers.Add(lines[i]);

            string bodyText = string.Join(" ", lines.Skip(maxIndex));

            string headerMatch = "";
            foreach (string header in headers)
            {
                // Strict check for headers usually works better
                if (ContentDict.ContainsKey(header))
                {
                    if (!string.IsNullOrEmpty(headerMatch)) headerMatch += " ";
                    headerMatch += ContentDict[header];
                }
                else
                {
                    // Optional: Try fuzzy match for header too? Usually overkill.
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
            if (!char.IsUpper(text[0])) return false;
            return true;
        }

        private static bool IsEnglish(string text)
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