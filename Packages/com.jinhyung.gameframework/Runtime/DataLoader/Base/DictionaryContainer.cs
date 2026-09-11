using System.Collections.Generic;
using UnityEngine;

namespace Game_DataLoader
{
    public abstract class DictionaryContainer<TKey, TValue>
        : DataContainer<TKey, TValue>
        where TValue : class, IDataKey<TKey>, IData
    {
        protected Dictionary<TKey, TValue> _dict;
        private List<TValue> _valuesCache;

        public override int Count
        {
            get
            {
                if (_dict == null)
                {
                    return 0;
                }
                return _dict.Count;
            }
        }

        public IReadOnlyDictionary<TKey, TValue> All
        {
            get
            {
                return _dict;
            }
        }

        public IReadOnlyList<TValue> AllValues
        {
            get
            {
                if (_dict == null)
                {
                    return null;
                }
                if (_valuesCache == null)
                {
                    _valuesCache = new List<TValue>(_dict.Values);
                }
                return _valuesCache;
            }
        }

        public virtual TValue Get(TKey key)
        {
            if (_dict == null)
            {
                return null;
            }
            _dict.TryGetValue(key, out TValue value);
            return value;
        }

        public bool TryGet(TKey key, out TValue value)
        {
            if (_dict == null)
            {
                value = null;
                return false;
            }
            return _dict.TryGetValue(key, out value);
        }

        public bool ContainsKey(TKey key)
        {
            if (_dict == null)
            {
                return false;
            }
            return _dict.ContainsKey(key);
        }

        protected override void MainCollectionConstructor(int count)
        {
            IEqualityComparer<TKey> comparer = GetEqualityComparer();
            _dict = comparer != null
                ? new Dictionary<TKey, TValue>(count, comparer)
                : new Dictionary<TKey, TValue>(count);
            _valuesCache = null;
        }

        protected override void MainCollectionAdd(TKey key, TValue value)
        {
            if (_dict.ContainsKey(key))
            {
                Debug.LogError($"{Name} DB의 id '{key}'가 중복되었습니다");
                return;
            }
            _dict.Add(key, value);
        }

        protected override void SubCollectionConstructor(int count)
        {
        }

        protected override void SubCollectionAdd(TKey key, TValue value)
        {
        }

        protected override void OnLoadCompleted()
        {
        }

        public override void Clear()
        {
            base.Clear();
            if (_dict != null)
            {
                _dict.Clear();
            }
            _valuesCache = null;

            SubCollectionConstructor(0);
        }
    }
}
