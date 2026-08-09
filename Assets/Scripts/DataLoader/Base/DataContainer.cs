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

        /// <summary>
        /// 보조 자료구조 생성 훅. 매 로드마다 Main보다 뒤에 불린다.
        /// Id 외의 컬럼으로 조회해야 하는 컨테이너가 여기서 필요한 딕셔너리를 만든다.
        /// 로드마다 새로 만들므로 재로드 시 중복 누적이 없다.
        /// </summary>
        protected abstract void SubCollectionConstructor(int count);

        /// <summary>
        /// 보조 자료구조 적재 훅. 행마다 MainCollectionAdd 뒤에 불린다.
        /// </summary>
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
