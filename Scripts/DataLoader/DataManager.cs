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

        private static void LogGameDataLabelMissing(string detail)
        {
            // 라벨/그룹 설정이 안 된 상태다. 예외를 그대로 흘리면 InitializeAsync를 await하지 않는
            // 호출부에서 통째로 삼켜져 "데이터가 왜 안 뜨지" 상태가 되므로, 여기서 잡아 원인을 찍는다.
            Debug.LogError(
                $"[DataManager] Addressables 라벨 '{GameDataLabel}'로 JSON을 불러오지 못했습니다. ({detail})\n" +
                "Window > Asset Management > Addressables > Groups 에서 Assets/GameData 의 JSON을 그룹에 등록하고 " +
                $"라벨 '{GameDataLabel}'을 붙였는지 확인하세요.");
        }

        private static async Task<Dictionary<string, string>> LoadJsonTextByNameAsync(CancellationToken cancellationToken)
        {
            AsyncOperationHandle<IList<TextAsset>> handle;
            try
            {
                handle = Addressables.LoadAssetsAsync<TextAsset>(GameDataLabel, null);
            }
            catch (Exception e)
            {
                LogGameDataLabelMissing($"{e.GetType().Name} {e.Message}");
                return new Dictionary<string, string>(0, StringComparer.Ordinal);
            }

            try
            {
                IList<TextAsset> assets;
                try
                {
                    assets = await handle.Task;
                }
                catch (Exception e)
                {
                    LogGameDataLabelMissing($"{e.GetType().Name} {e.Message}");
                    return new Dictionary<string, string>(0, StringComparer.Ordinal);
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (handle.Status == AsyncOperationStatus.Failed || assets == null)
                {
                    LogGameDataLabelMissing(handle.OperationException != null
                        ? handle.OperationException.Message
                        : "결과가 비어 있습니다");
                    return new Dictionary<string, string>(0, StringComparer.Ordinal);
                }

                var map = new Dictionary<string, string>(assets.Count, StringComparer.Ordinal);

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
