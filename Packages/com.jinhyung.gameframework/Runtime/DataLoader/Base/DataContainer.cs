using System.Collections.Generic;

namespace Game_DataLoader
{
    public abstract class DataContainer : IDataContainer
    {
        public abstract string Name { get; }
        public abstract int Count { get; }
        public bool Loaded { get; private set; }

        public abstract void LoadJson(string text);

        protected void SetLoaded(bool loaded)
        {
            Loaded = loaded;
        }

        public virtual void Clear()
        {
            SetLoaded(false);
        }

        public virtual bool Validate(out string errorMessage)
        {
            errorMessage = null;
            return true;
        }

        public virtual void AfterAllTableLoaded()
        {
        }
    }

    public abstract class DataContainer<TKey, TValue>
        : DataContainer
        where TValue : class, IDataKey<TKey>, IData
    {
        protected abstract void MainCollectionConstructor(int count);

        protected abstract void MainCollectionAdd(TKey key, TValue value);

        protected abstract void SubCollectionConstructor(int count);

        protected abstract void SubCollectionAdd(TKey key, TValue value);

        protected abstract void OnLoadCompleted();

        protected virtual IEqualityComparer<TKey> GetEqualityComparer()
        {
            return null;
        }

        protected List<TValue> Deserialize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }
            return Newtonsoft.Json.JsonConvert.DeserializeObject<List<TValue>>(text, JsonSettings.Default);
        }

        public override void LoadJson(string text)
        {
            List<TValue> list = Deserialize(text);
            if (list == null || list.Count == 0)
            {
                MainCollectionConstructor(0);
                SubCollectionConstructor(0);
                SetLoaded(true);
                OnLoadCompleted();
                return;
            }

            MainCollectionConstructor(list.Count);
            SubCollectionConstructor(list.Count);

            for (int i = 0; i < list.Count; i++)
            {
                TValue item = list[i];
                if (item == null)
                {
                    continue;
                }
                MainCollectionAdd(item.Key, item);
                SubCollectionAdd(item.Key, item);
            }

            SetLoaded(true);
            OnLoadCompleted();
        }
    }
}
