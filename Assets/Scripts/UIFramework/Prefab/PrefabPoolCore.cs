using System.Collections.Generic;
using UnityEngine;

namespace Game_UIFramework
{
    /// <summary>
    /// 경로 기반 프리팹 풀 코어
    /// PrefabAuto로 생성되는 BaseComponent를 경로 단위로 풀링한다
    /// Get으로 꺼내고 Release로 반환 (WindowKey의 윈도우 캐싱과 동일한 수명 관리)
    /// </summary>
    public sealed class PrefabPoolCore
    {
        private readonly Transform _root;
        private readonly Dictionary<string, Stack<BaseComponent>> _pooled = new Dictionary<string, Stack<BaseComponent>>();
        private readonly Dictionary<BaseComponent, string> _activePaths = new Dictionary<BaseComponent, string>();

        public PrefabPoolCore(Transform root)
        {
            _root = root;
        }

        /// <summary>
        /// 풀에서 꺼내거나 없으면 Resources에서 로드해 생성
        /// </summary>
        public T Get<T>(string path) where T : BaseComponent
        {
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError($"PrefabPool: {typeof(T).Name} Path is Null");
                return null;
            }

            if (_pooled.TryGetValue(path, out Stack<BaseComponent> stack))
            {
                while (stack.Count > 0)
                {
                    var pooled = stack.Pop();
                    if (pooled == null)
                    {
                        continue;
                    }

                    if ((pooled is T typed) == false)
                    {
                        Debug.LogError($"PrefabPool: {path} pooled instance is not {typeof(T).Name}");
                        Object.Destroy(pooled.gameObject);
                        continue;
                    }

                    _activePaths[typed] = path;
                    (typed as IPoolable)?.OnSpawn();
                    return typed;
                }
            }

            return CreateNew<T>(path);
        }

        /// <summary>
        /// 인스턴스를 풀에 반환 (비활성화 후 풀 루트로 이동)
        /// </summary>
        public bool Release(BaseComponent comp)
        {
            if (comp == null)
            {
                return false;
            }

            if (_activePaths.TryGetValue(comp, out string path) == false)
            {
                return false;
            }

            _activePaths.Remove(comp);

            (comp as IPoolable)?.OnDespawn();
            comp.gameObject.SetActive(false);
            comp.CachedTransform.SetParent(_root, false);

            if (_pooled.TryGetValue(path, out Stack<BaseComponent> stack) == false)
            {
                stack = new Stack<BaseComponent>();
                _pooled[path] = stack;
            }
            stack.Push(comp);

            return true;
        }

        /// <summary>
        /// 이 풀에서 꺼낸 활성 인스턴스인지 확인
        /// </summary>
        public bool Contains(BaseComponent comp)
        {
            return comp != null && _activePaths.ContainsKey(comp);
        }

        /// <summary>
        /// 초기 생성 후 풀에 채워둔다
        /// </summary>
        public void Preload<T>(string path, int count) where T : BaseComponent
        {
            for (int i = 0; i < count; i++)
            {
                var comp = CreateNew<T>(path);
                if (comp == null)
                {
                    return;
                }
                Release(comp);
            }
        }

        private T CreateNew<T>(string path) where T : BaseComponent
        {
            var prefab = Resources.Load<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogError($"PrefabPool: {typeof(T).Name} Prefab not found at path: {path}");
                return null;
            }

            var instance = Object.Instantiate(prefab, _root);
            instance.name = prefab.name;

            var comp = instance.GetComponent<T>();
            if (comp == null)
            {
                Debug.LogError($"PrefabPool: {typeof(T).Name} component not found on prefab: {path}");
                Object.Destroy(instance);
                return null;
            }

            _activePaths[comp] = path;
            (comp as IPoolable)?.OnSpawn();
            return comp;
        }
    }
}
