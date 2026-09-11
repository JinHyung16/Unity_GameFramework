using UnityEngine;

namespace Game_UIFramework
{
    public static class WindowFactory
    {
        public static T LoadWindow<T>(WindowKey key, Transform parent = null) where T : BaseWindow
        {
            if (string.IsNullOrEmpty(key.Path))
            {
                Debug.LogError($"WindowFactory: {typeof(T).Name} Path is Null");
                return null;
            }

            var prefab = Resources.Load<GameObject>(key.Path);
            if (prefab == null)
            {
                Debug.LogError($"WindowFactory: {typeof(T).Name} Prefab not found at path: {key.Path}");
                return null;
            }

            var instance = parent != null
                ? Object.Instantiate(prefab, parent)
                : Object.Instantiate(prefab);

            var window = instance.GetComponent<T>();
            if (window == null)
            {
                Debug.LogError($"WindowFactory: {typeof(T).Name} component not found on prefab: {key.Path}");
                Object.Destroy(instance);
                return null;
            }

            window.gameObject.SetActive(false);
            return window;
        }
    }
}
