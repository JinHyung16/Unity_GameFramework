using System.Collections.Generic;
using UnityEngine;

namespace Game_UIFramework
{
    public sealed class PrefabPoolCore
    {
        private readonly Transform _root;
        private readonly Dictionary<string, Stack<BaseComponent>> _pooled = new Dictionary<string, Stack<BaseComponent>>();
        private readonly Dictionary<BaseComponent, string> _activePaths = new Dictionary<BaseComponent, string>();

        public PrefabPoolCore(Transform root)
        {
            _root = root;
        }

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

        public bool Contains(BaseComponent comp)
        {
            return comp != null && _activePaths.ContainsKey(comp);
        }

        public void Preload<T>(string path, int count) where T : BaseComponent
        {
            if (_pooled.TryGetValue(path, out Stack<BaseComponent> stack) == false)
            {
                stack = new Stack<BaseComponent>();
                _pooled[path] = stack;
            }

            for (int i = 0; i < count; i++)
            {
                var comp = InstantiateNew<T>(path);
                if (comp == null)
                {
                    return;
                }
                comp.gameObject.SetActive(false);
                comp.CachedTransform.SetParent(_root, false);
                stack.Push(comp);
            }
        }

        private T CreateNew<T>(string path) where T : BaseComponent
        {
            var comp = InstantiateNew<T>(path);
            if (comp == null)
            {
                return null;
            }

            _activePaths[comp] = path;
            (comp as IPoolable)?.OnSpawn();
            return comp;
        }

        private T InstantiateNew<T>(string path) where T : BaseComponent
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

            return comp;
        }
    }
}
