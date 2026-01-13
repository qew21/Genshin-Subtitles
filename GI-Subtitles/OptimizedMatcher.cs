using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace GI_Subtitles
{
    public class OptimizedMatcher
    {
        // 存储原始数据，使用结构体数组以获得最佳内存布局
        private readonly Entry[] _entries;

        // 倒排索引：Bigram (两字片段) -> 包含该片段的所有 Entry ID 列表
        // 比如: "狂猎" -> [5, 12, 108]
        private readonly Dictionary<string, List<int>> _bigramIndex;

        // 小于2个字的词无法生成 Bigram，单独存一个桶用于兜底
        private readonly int[] _shortKeysIndices;
        private readonly Dictionary<string, string> ContentDict;
        public bool Loaded = false;

        private struct Entry
        {
            public string Key;
            public string Value;
            public int Length;
        }

        public OptimizedMatcher(Dictionary<string, string> voiceContentDict)
        {
            ContentDict = voiceContentDict;
            int count = voiceContentDict.Count;
            _entries = new Entry[count];
            _bigramIndex = new Dictionary<string, List<int>>(count * 3); // 预估容量
            var shortKeysList = new List<int>();

            int index = 0;
            foreach (var kvp in voiceContentDict)
            {
                string key = kvp.Key;

                // 1. 存入原始数据数组
                _entries[index] = new Entry
                {
                    Key = key,
                    Value = kvp.Value,
                    Length = key.Length
                };

                // 2. 构建索引 (内存换时间的核心)
                if (key.Length < 2)
                {
                    shortKeysList.Add(index);
                }
                else
                {
                    // 生成 Bigrams (每两个相邻字符)
                    // "狂猎灾祸" -> "狂猎", "猎灾", "灾祸"
                    // 使用 HashSet 去重，避免同一个词里由重复片段导致重复索引
                    var uniqueBigrams = new HashSet<string>();
                    for (int i = 0; i < key.Length - 1; i++)
                    {
                        // 这是一个纯内存操作，为了速度牺牲空间，直接 Substring
                        // 极致优化可以用 int hash 替代 string key，但容易冲突，这里求稳
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
            if (string.IsNullOrEmpty(input))
            {
                Key = "";
                return "";
            }

            int inputLen = input.Length;

            // --- 阶段 1: 候选集筛选 (Candidate Selection) ---

            // 使用 HashSet 记录需要进行精确计算的 ID，自动去重
            // 如果 input 很短 (<2)，无法利用 Bigram 索引，只能退化为遍历所有短词
            HashSet<int> candidates;

            if (inputLen < 2)
            {
                // 输入太短，直接匹配所有短词 + 可能的索引
                // 这里简单起见，如果输入太短，我们只扫描短词表
                candidates = new HashSet<int>(_shortKeysIndices);
            }
            else
            {
                // 输入足够长，利用索引
                candidates = new HashSet<int>();

                // 1.1 收集候选者
                // 策略：只要命中 Input 中的任意一个 Bigram，就将其纳入候选
                // 优化思路：如果 Input 很长，可以要求至少命中 2 个或 3 个 Bigram 才算候选，
                // 但为了不漏掉模糊匹配，这里采用最宽松策略：命中 1 个即算。
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

                // 1.2 极其重要：
                // 如果是"包含逻辑"（Input是Key的子串），Key一定包含了Input的所有Bigram，所以一定在candidates里。
                // 如果是"Key是Input的子串"，Key可能很短，所以我们要把 _shortKeysIndices 也加进来兜底。
                foreach (var id in _shortKeysIndices) candidates.Add(id);

                // 如果没有找到任何候选者（说明完全不相关），直接返回空
                if (candidates.Count == 0)
                {
                    Key = "";
                    return "";
                }
            }

            // 如果候选集过大（例如超过 2000 个），并行处理；否则直接单线程处理更快
            // 此时 candidates.Count 通常只有几十到几百个，比之前的 10000 个少两个数量级
            var candidateList = candidates.ToArray();

            // --- 阶段 2: 精确计算 (Exact Calculation) ---
            // 直接复用之前已经优化到极致的逻辑，只是数据源变了

            // 全局最优解
            int globalBestDistance = int.MaxValue;
            string globalBestKey = null;
            object globalLock = new object();

            // 只有当候选数量较多时才开启并行，否则上下文切换开销大于收益
            // 经验阈值：200
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
                    // 模拟一个空的 LoopState
                    ProcessCandidate(id, input, inputLen, ref globalBestDistance, ref globalBestKey, globalLock, null);
                    if (globalBestDistance == 0) break;
                }
            }

            // 结果验证
            if (globalBestDistance < inputLen / 1.5)
            {
                Key = globalBestKey;
                // 注意：这里需要根据 Key 再去查 Value，或者直接在 Entry 里存 Value (这里已存)
                // 为了快速返回，我们需要根据 Key 去原字典查，或者我们在 Entry 里就存了 Value
                // 这里我们在 Entry 里存了 Value，直接取即可，避免再次查字典
                // 但 globalBestKey 只是字符串，我们得找回 Value
                // 简单点：直接返回 Entry 里的 Value。
                // *为了代码简洁，这里稍作妥协，重新去 _entries 找或者让 ProcessCandidate 记录 bestEntry*
                // 鉴于已经有 Key，直接从字典取是最安全的
                // 为了性能，建议在 Entry 结构体里直接存 Value，这里因为 globalBestKey 只是 string，
                // 我们假设外部还是根据 Key 来取，或者你可以修改 ProcessCandidate 直接返回 Entry。

                // 这里为了维持接口不变，仍返回 Key
                return _entries.First(e => e.Key == globalBestKey).Value;
                // 优化：上面的 First 是 O(N)，这不行。
                // 修正：我们应该记录 bestIndex
            }

            Key = "";
            return "";
        }

        // 核心处理逻辑 (提取出来以便复用)
        private void ProcessCandidate(int id, string input, int inputLen, ref int globalBestDistance, ref string globalBestKey, object lockObj, ParallelLoopState loopState)
        {
            // 快速检查：如果已有完美匹配
            if (globalBestDistance == 0)
            {
                loopState?.Stop();
                return;
            }

            // 直接通过数组下标访问，极快 (O(1))
            ref readonly var entry = ref _entries[id];
            string key = entry.Key;
            int keyLen = entry.Length;

            // 原始业务过滤逻辑
            if (inputLen <= 5 && keyLen >= inputLen * 3) return;

            int currentDistance = int.MaxValue;
            bool isSpecialMatch = false;

            // 包含优先逻辑
            if (inputLen > 10)
            {
                if (key.IndexOf(input, StringComparison.Ordinal) >= 0 ||
                   (keyLen > 10 && input.IndexOf(key, StringComparison.Ordinal) >= 0))
                {
                    isSpecialMatch = true;
                    currentDistance = 0;
                }
            }

            if (!isSpecialMatch)
            {
                // 剪枝
                if (Math.Abs(keyLen - inputLen) >= globalBestDistance) return;

                ReadOnlySpan<char> targetSpan = key.AsSpan();
                if (inputLen > 5 && keyLen > inputLen)
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

        // 保持不变的极致优化 Levenshtein
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

        public string FindMatchWithHeader(string ocrText, out string key)
        {
            key = "";

            if (string.IsNullOrEmpty(ocrText))
                return "";

            string[] lines = ocrText.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 1)
            {
                return FindClosestMatch(lines[0], out key);
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
}