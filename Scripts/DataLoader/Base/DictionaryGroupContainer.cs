using System.Collections.Generic;

namespace Game_DataLoader
{
    public abstract class DictionaryGroupContainer<TKey, TValue>
        : DataContainer<TKey, TValue>
        where TValue : class, IDataKey<TKey>, IData
    {
        protected Dictionary<TKey, List<TValue>> _groups;
        private Dictionary<TKey, IReadOnlyList<TValue>> _groupsView;
        private int _count;

        public override int Count => _count;

        public IReadOnlyDictionary<TKey, IReadOnlyList<TValue>> All
        {
            get
            {
                if (_groups == null)
                {
                    return null;
                }
                if (_groupsView == null)
                {
                    _groupsView = new Dictionary<TKey, IReadOnlyList<TValue>>(_groups.Count, _groups.Comparer);
                    foreach (KeyValuePair<TKey, List<TValue>> pair in _groups)
                    {
                        _groupsView.Add(pair.Key, pair.Value);
                    }
                }
                return _groupsView;
            }
        }

        public IReadOnlyList<TValue> Get(TKey key)
        {
            if (_groups == null)
            {
                return null;
            }
            _groups.TryGetValue(key, out List<TValue> list);
            return list;
        }

        public bool TryGet(TKey key, out IReadOnlyList<TValue> list)
        {
            if (_groups == null)
            {
                list = null;
                return false;
            }

            if (_groups.TryGetValue(key, out List<TValue> found))
            {
                list = found;
                return true;
            }

            list = null;
            return false;
        }

        public bool ContainsKey(TKey key)
        {
            return _groups != null && _groups.ContainsKey(key);
        }

        protected override void MainCollectionConstructor(int count)
        {
            IEqualityComparer<TKey> comparer = GetEqualityComparer();
            _groups = comparer != null
                ? new Dictionary<TKey, List<TValue>>(comparer)
                : new Dictionary<TKey, List<TValue>>();
            _groupsView = null;
            _count = 0;
        }

        protected override void MainCollectionAdd(TKey key, TValue value)
        {
            if (_groups.TryGetValue(key, out List<TValue> list) == false)
            {
                list = new List<TValue>();
                _groups.Add(key, list);
            }
            list.Add(value);
            _count++;
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
            if (_groups != null)
            {
                _groups.Clear();
            }
            _groupsView = null;
            _count = 0;
            SubCollectionConstructor(0);
        }
    }
}
