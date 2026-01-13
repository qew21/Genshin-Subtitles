using System;
using System.Collections.Generic;
using System.Linq;

namespace GI_Subtitles
{
    /// <summary>
    /// LRU (Least Recently Used) 缓存实现
    /// 用于限制缓存大小，自动移除最久未使用的项
    /// </summary>
    public class LRUCache<TKey, TValue>
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, LinkedListNode<CacheItem>> _cache;
        private readonly LinkedList<CacheItem> _list;

        private class CacheItem
        {
            public TKey Key { get; set; }
            public TValue Value { get; set; }
        }

        public LRUCache(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentException("Capacity must be greater than 0", nameof(capacity));

            _capacity = capacity;
            _cache = new Dictionary<TKey, LinkedListNode<CacheItem>>(capacity);
            _list = new LinkedList<CacheItem>();
        }

        /// <summary>
        /// 获取缓存值，如果存在则将其移到最前面（标记为最近使用）
        /// </summary>
        public bool TryGetValue(TKey key, out TValue value)
        {
            value = default(TValue);
            if (!_cache.TryGetValue(key, out var node))
                return false;

            // 移到最前面（标记为最近使用）
            _list.Remove(node);
            _list.AddFirst(node);

            value = node.Value.Value;
            return true;
        }

        /// <summary>
        /// 添加或更新缓存项
        /// </summary>
        public void AddOrUpdate(TKey key, TValue value)
        {
            if (_cache.TryGetValue(key, out var existingNode))
            {
                // 更新现有项
                existingNode.Value.Value = value;
                _list.Remove(existingNode);
                _list.AddFirst(existingNode);
            }
            else
            {
                // 添加新项
                if (_cache.Count >= _capacity)
                {
                    // 移除最久未使用的项（最后一个）
                    var lastNode = _list.Last;
                    if (lastNode != null)
                    {
                        _cache.Remove(lastNode.Value.Key);
                        _list.RemoveLast();
                    }
                }

                var newNode = new LinkedListNode<CacheItem>(new CacheItem { Key = key, Value = value });
                _list.AddFirst(newNode);
                _cache[key] = newNode;
            }
        }

        /// <summary>
        /// 检查是否包含指定的键
        /// </summary>
        public bool ContainsKey(TKey key)
        {
            if (!_cache.TryGetValue(key, out var node))
                return false;

            // 移到最前面（标记为最近使用）
            _list.Remove(node);
            _list.AddFirst(node);
            return true;
        }

        /// <summary>
        /// 移除指定的键
        /// </summary>
        public bool Remove(TKey key)
        {
            if (!_cache.TryGetValue(key, out var node))
                return false;

            _cache.Remove(key);
            _list.Remove(node);
            return true;
        }

        /// <summary>
        /// 获取所有键
        /// </summary>
        public IEnumerable<TKey> Keys => _cache.Keys;

        /// <summary>
        /// 获取缓存项数量
        /// </summary>
        public int Count => _cache.Count;

        /// <summary>
        /// 清空缓存
        /// </summary>
        public void Clear()
        {
            _cache.Clear();
            _list.Clear();
        }

        /// <summary>
        /// 获取或设置缓存值（类似字典的索引器）
        /// </summary>
        public TValue this[TKey key]
        {
            get
            {
                if (TryGetValue(key, out var value))
                    return value;
                throw new KeyNotFoundException($"Key '{key}' not found in cache");
            }
            set => AddOrUpdate(key, value);
        }
    }
}






