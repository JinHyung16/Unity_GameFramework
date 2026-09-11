using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game_UIFramework
{
    public class ScenePrefabPool : MonoSceneSingleton<ScenePrefabPool>
    {
        public PrefabPoolCore Core { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            Core = new PrefabPoolCore(transform);
        }

        protected override void OnDestroy()
        {
            Core?.Clear();
            base.OnDestroy();
        }
    }

    public class GlobalPrefabPool : MonoSingleton<GlobalPrefabPool>
    {
        public PrefabPoolCore Core { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            Core = new PrefabPoolCore(transform);

            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        protected override void OnDestroy()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            Core?.Clear();
            base.OnDestroy();
        }

        private void OnSceneUnloaded(Scene scene)
        {
            Core?.PruneDestroyed();
        }
    }

    public static class PrefabLoader
    {
        public static T Get<T>(string path) where T : BaseComponent
        {
            return ScenePrefabPool.Instance.Core.Get<T>(path);
        }

        public static T GetGlobal<T>(string path) where T : BaseComponent
        {
            return GlobalPrefabPool.Instance.Core.Get<T>(path);
        }

        public static void Preload<T>(string path, int count, bool isGlobal) where T : BaseComponent
        {
            if (isGlobal)
            {
                GlobalPrefabPool.Instance.Core.Preload<T>(path, count);
            }
            else
            {
                ScenePrefabPool.Instance.Core.Preload<T>(path, count);
            }
        }

        public static void Release(BaseComponent comp)
        {
            if (comp == null)
            {
                return;
            }

            if (GlobalPrefabPool.IsValidInstance && GlobalPrefabPool.Instance.Core.Release(comp))
            {
                return;
            }

            if (ScenePrefabPool.IsValidInstance && ScenePrefabPool.Instance.Core.Release(comp))
            {
                return;
            }

            Debug.LogWarning($"PrefabLoader: {comp.name} is not from pool. Destroyed.");
            Object.Destroy(comp.gameObject);
        }

        public static void PruneDestroyed()
        {
            if (GlobalPrefabPool.IsValidInstance)
            {
                GlobalPrefabPool.Instance.Core.PruneDestroyed();
            }

            if (ScenePrefabPool.IsValidInstance)
            {
                ScenePrefabPool.Instance.Core.PruneDestroyed();
            }
        }

        public static void ClearScenePool()
        {
            if (ScenePrefabPool.IsValidInstance)
            {
                ScenePrefabPool.Instance.Core.Clear();
            }
        }
    }
}
