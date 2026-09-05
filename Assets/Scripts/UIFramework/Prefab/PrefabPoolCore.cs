using System.Collections.Generic;
using UnityEngine;

namespace Game_UIFramework
{
    public sealed class PrefabPoolCore
    {
        private readonly Transform _root;
        private readonly Dictionary<string, Stack<BaseComponent>> _pooled = new Dictionary<string, Stack<BaseComponent>>();

        private readonly Dictionary<int, ActiveInfo> _active = new Dictionary<int, ActiveInfo>();

        private readonly List<int> _pruneBuffer = new List<int>();
        private readonly List<BaseComponent> _compactBuffer = new List<BaseComponent>();

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

                    _active[typed.GetInstanceID()] = new ActiveInfo(typed, path);
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

            int id = comp.GetInstanceID();
            if (_active.TryGetValue(id, out ActiveInfo info) == false)
            {
                return false;
            }

            _active.Remove(id);

            (comp as IPoolable)?.OnDespawn();
            comp.gameObject.SetActive(false);
            comp.CachedTransform.SetParent(_root, false);

            if (_pooled.TryGetValue(info.Path, out Stack<BaseComponent> stack) == false)
            {
                stack = new Stack<BaseComponent>();
                _pooled[info.Path] = stack;
            }
            stack.Push(comp);

            return true;
        }

        public bool Contains(BaseComponent comp)
        {
            return comp != null && _active.ContainsKey(comp.GetInstanceID());
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

        public int PruneDestroyed()
        {
            _pruneBuffer.Clear();
            foreach (var pair in _active)
            {
                if (pair.Value.Comp == null)
                {
                    _pruneBuffer.Add(pair.Key);
                }
            }

            for (int i = 0; i < _pruneBuffer.Count; i++)
            {
                _active.Remove(_pruneBuffer[i]);
            }

            int removed = _pruneBuffer.Count;
            _pruneBuffer.Clear();

            CompactPooled();
            return removed;
        }

        public void Clear()
        {
            foreach (var pair in _pooled)
            {
                var stack = pair.Value;
                while (stack.Count > 0)
                {
                    var comp = stack.Pop();
                    if (comp != null)
                    {
                        Object.Destroy(comp.gameObject);
                    }
                }
            }

            _pooled.Clear();
            _active.Clear();
            _pruneBuffer.Clear();
            _compactBuffer.Clear();
        }

        private void CompactPooled()
        {
            foreach (var pair in _pooled)
            {
                var stack = pair.Value;
                if (stack.Count == 0)
                {
                    continue;
                }

                _compactBuffer.Clear();
                while (stack.Count > 0)
                {
                    var comp = stack.Pop();
                    if (comp != null)
                    {
                        _compactBuffer.Add(comp);
                    }
                }

                for (int i = _compactBuffer.Count - 1; i >= 0; i--)
                {
                    stack.Push(_compactBuffer[i]);
                }
            }

            _compactBuffer.Clear();
        }

        private T CreateNew<T>(string path) where T : BaseComponent
        {
            var comp = InstantiateNew<T>(path);
            if (comp == null)
            {
                return null;
            }

            _active[comp.GetInstanceID()] = new ActiveInfo(comp, path);
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

        private readonly struct ActiveInfo
        {
            public readonly BaseComponent Comp;
            public readonly string Path;

            public ActiveInfo(BaseComponent comp, string path)
            {
                Comp = comp;
                Path = path;
            }
        }
    }
}
