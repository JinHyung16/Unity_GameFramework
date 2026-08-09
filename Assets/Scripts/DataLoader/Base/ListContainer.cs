using System;
using System.Collections.Generic;

namespace Game_DataLoader
{
    public abstract class ListContainer<TKey, TValue>
        : DataContainer<TKey, TValue>
        where TValue : class, IDataKey<TKey>, IData
        where TKey : IEquatable<TKey>
    {
        protected List<TValue> _list;
        protected Dictionary<TKey, TValue> _dictByKey;

        public override int Count
        {
            get
            {
                if (_list == null)
                {
                    return 0;
                }
                return _list.Count;
            }
        }

        public IReadOnlyList<TValue> All
        {
            get
            {
                return _list;
            }
        }

        public TValue GetByKey(TKey key)
        {
            if (_list == null)
            {
                return null;
            }

            if (_dictByKey.TryGetValue(key, out TValue value))
            {
                return value;
            }

            for (int i = 0; i < _list.Count; i++)
            {
                TValue item = _list[i];
                if (item == null)
                {
                    continue;
                }
                if (item.Key.Equals(key) == false)
                {
                    continue;
                }

                _dictByKey.Add(key, item);
                return item;
            }

            return null;
        }

        public TValue GetByIndex(int index)
        {
            if (_list == null)
            {
                return null;
            }
            if (index < 0)
            {
                return null;
            }
            if (index >= _list.Count)
            {
                return null;
            }
            return _list[index];
        }

        protected override void MainCollectionConstructor(int count)
        {
            _list = new List<TValue>(count);
            IEqualityComparer<TKey> comparer = GetEqualityComparer();
            _dictByKey = comparer != null
                ? new Dictionary<TKey, TValue>(count, comparer)
                : new Dictionary<TKey, TValue>(count);
        }

        protected override void MainCollectionAdd(TKey key, TValue value)
        {
            _list.Add(value);
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
            if (_list != null)
            {
                _list.Clear();
            }
            if (_dictByKey != null)
            {
                _dictByKey.Clear();
            }
            SubCollectionConstructor(0);
        }
    }
}
