using UnityEngine;

namespace Game_UIFramework
{
    /// <summary>
    /// 씬 프리팹 풀 (씬 전환 시 풀과 인스턴스가 함께 해제)
    /// </summary>
    public class ScenePrefabPool : MonoSceneSingleton<ScenePrefabPool>
    {
        public PrefabPoolCore Core { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            Core = new PrefabPoolCore(transform);
        }
    }

    /// <summary>
    /// 글로벌 프리팹 풀 (씬 전환에도 유지)
    /// </summary>
    public class GlobalPrefabPool : MonoSingleton<GlobalPrefabPool>
    {
        public PrefabPoolCore Core { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            Core = new PrefabPoolCore(transform);
        }
    }

    /// <summary>
    /// 프리팹 풀 정적 파사드
    /// PrefabAuto가 사용하는 로드/반환 진입점
    /// </summary>
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

        /// <summary>
        /// 소속 풀을 찾아 반환. 풀 소속이 아니면 파괴한다
        /// </summary>
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
    }
}
