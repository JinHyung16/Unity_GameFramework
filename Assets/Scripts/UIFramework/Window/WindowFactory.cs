using UnityEngine;

namespace Game_UIFramework
{
    /// <summary>
    /// 윈도우 생성 팩토리 클래스
    /// 윈도우 프리팹을 로드하고 초기화하는 역할
    /// </summary>
    public static class WindowFactory
    {
        /// <summary>
        /// 윈도우 로드 (Resources 폴더에서)
        /// </summary>
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

            window.gameObject.SetActive(true);
            return window;
        }
    }
}



