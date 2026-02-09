using System;
using System.Collections.Generic;
using System.Linq;

namespace GI_Subtitles
{
    /// <summary>
    /// LRU (Least Recently Used) cache implementation
    /// To limit the cache size, automatically remove the least recently used items
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
        /// Get the cache value, if it exists, move it to the front (marked as recently used)
        /// </summary>
        public bool TryGetValue(TKey key, out TValue value)
        {
            value = default(TValue);
            if (!_cache.TryGetValue(key, out var node))
                return false;

            // Move to the front (marked as recently used)
            _list.Remove(node);
            _list.AddFirst(node);

            value = node.Value.Value;
            return true;
        }

        /// <summary>
        /// Add or update the cache item
        /// </summary>
        public void AddOrUpdate(TKey key, TValue value)
        {
            if (_cache.TryGetValue(key, out var existingNode))
            {
                // Update the existing item
                existingNode.Value.Value = value;
                _list.Remove(existingNode);
                _list.AddFirst(existingNode);
            }
            else
            {
                // Add a new item
                if (_cache.Count >= _capacity)
                {
                    // Remove the least recently used item (last one)
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
        /// Check if the specified key is included
        /// </summary>
        public bool ContainsKey(TKey key)
        {
            if (!_cache.TryGetValue(key, out var node))
                return false;

            // Move to the front (marked as recently used)
            _list.Remove(node);
            _list.AddFirst(node);
            return true;
        }

        /// <summary>
        /// Remove the specified key
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
        /// Get all keys
        /// </summary>
        public IEnumerable<TKey> Keys => _cache.Keys;

        /// <summary>
        /// Get the number of cache items
        /// </summary>
        public int Count => _cache.Count;

        /// <summary>
        /// Clear the cache
        /// </summary>
        public void Clear()
        {
            _cache.Clear();
            _list.Clear();
        }

        /// <summary>
        /// Get or set the cache value (similar to the indexer of a dictionary)
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

