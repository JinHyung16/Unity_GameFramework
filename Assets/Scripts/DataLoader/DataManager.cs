using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Game_DataLoader
{
    public class DataManager
    {
        // Assets/GameData 폴더 엔트리에 부여한 Addressables 라벨.
        // 이 라벨이 붙은 모든 JSON(TextAsset)을 한 번에 로드한다.
        public const string GameDataLabel = "game_data";

        private static DataManager _instance;

        public static DataManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new DataManager();
                }
                return _instance;
            }
        }

        private readonly Dictionary<Type, IDataContainer> _containers = new Dictionary<Type, IDataContainer>(16);
        private bool _initialized;

        public bool IsInitialized => _initialized;
        public int ContainerCount => _containers.Count;

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (_initialized)
            {
                return;
            }

            DiscoverContainers();

            Dictionary<string, string> jsonByName = await LoadJsonTextByNameAsync(cancellationToken);

            foreach (KeyValuePair<Type, IDataContainer> kv in _containers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                IDataContainer container = kv.Value;
                if (container == null)
                {
                    continue;
                }

                if (jsonByName.TryGetValue(container.Name, out string text) == false)
                {
                    Debug.LogWarning($"[DataManager] '{container.Name}' JSON이 없습니다 ({kv.Key.Name})");
                    container.Clear();
                    continue;
                }

                try
                {
                    container.LoadJson(text);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[DataManager] '{container.Name}' 로드 실패: {e.GetType().Name} {e.Message}");
                    container.Clear();
                }

                await Task.Yield();
            }

            ValidateAll();
            AfterAllLoaded();
            _initialized = true;
        }

        public T GetContainer<T>() where T : class, IDataContainer
        {
            if (_containers.TryGetValue(typeof(T), out IDataContainer container))
            {
                return container as T;
            }
            return null;
        }

        public IEnumerable<IDataContainer> AllContainers => _containers.Values;

        public IDataContainer GetContainerByName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }
            foreach (IDataContainer container in _containers.Values)
            {
                if (container != null && container.Name == name)
                {
                    return container;
                }
            }
            return null;
        }

        public void Clear()
        {
            foreach (KeyValuePair<Type, IDataContainer> kv in _containers)
            {
                if (kv.Value == null)
                {
                    continue;
                }
                kv.Value.Clear();
            }
            _initialized = false;
        }

        private static async Task<Dictionary<string, string>> LoadJsonTextByNameAsync(CancellationToken cancellationToken)
        {
            AsyncOperationHandle<IList<TextAsset>> handle =
                Addressables.LoadAssetsAsync<TextAsset>(GameDataLabel, null);

            try
            {
                IList<TextAsset> assets = await handle.Task;

                cancellationToken.ThrowIfCancellationRequested();

                int capacity = assets != null ? assets.Count : 0;
                var map = new Dictionary<string, string>(capacity, StringComparer.Ordinal);

                if (assets == null)
                {
                    return map;
                }

                for (int i = 0; i < assets.Count; i++)
                {
                    TextAsset asset = assets[i];
                    if (asset == null)
                    {
                        continue;
                    }
                    // text는 string으로 복사되므로, 아래 finally에서 핸들을 해제해도 안전하다.
                    map[asset.name] = asset.text;
                }
                return map;
            }
            finally
            {
                // JSON 텍스트만 필요하므로 로드한 TextAsset 메모리는 즉시 회수한다.
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
            }
        }

        private void DiscoverContainers()
        {
            _containers.Clear();

            Type baseType = typeof(DataContainer);
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            for (int a = 0; a < assemblies.Length; a++)
            {
                Type[] types = TryGetTypes(assemblies[a]);
                if (types == null)
                {
                    continue;
                }

                for (int i = 0; i < types.Length; i++)
                {
                    Type t = types[i];
                    if (t == null)
                    {
                        continue;
                    }
                    if (t.IsAbstract)
                    {
                        continue;
                    }
                    if (baseType.IsAssignableFrom(t) == false)
                    {
                        continue;
                    }
                    if (_containers.ContainsKey(t))
                    {
                        continue;
                    }

                    IDataContainer instance = TryCreateContainer(t);
                    if (instance == null)
                    {
                        continue;
                    }
                    _containers[t] = instance;
                }
            }
        }

        private void ValidateAll()
        {
            foreach (KeyValuePair<Type, IDataContainer> kv in _containers)
            {
                IDataContainer container = kv.Value;
                if (container == null || container.Loaded == false)
                {
                    continue;
                }
                if (container.Validate(out string errorMessage) == false)
                {
                    Debug.LogError($"[DataManager] '{container.Name}' 검증 실패: {errorMessage}");
                }
            }
        }

        private void AfterAllLoaded()
        {
            foreach (KeyValuePair<Type, IDataContainer> kv in _containers)
            {
                IDataContainer container = kv.Value;
                if (container == null || container.Loaded == false)
                {
                    continue;
                }
                container.AfterAllTableLoaded();
            }
        }

        private static Type[] TryGetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DataManager] {assembly.FullName} 타입 로드 실패: {e.Message}");
                return null;
            }
        }

        private static IDataContainer TryCreateContainer(Type t)
        {
            try
            {
                return (IDataContainer)Activator.CreateInstance(t);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DataManager] {t.Name} 생성 실패: {e.Message}");
                return null;
            }
        }
    }
}
