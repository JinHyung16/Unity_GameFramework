using UnityEngine;

namespace Game_UIFramework
{
    /// <summary>
    /// 간단한 MonoBehaviour 싱글톤 베이스 클래스
    /// </summary>
    public abstract class MonoSingleton<T> : MonoBehaviour where T : MonoSingleton<T>
    {
        public static T Instance
        {
            get
            {
                if (_isDestroyed)
                    return null;

                if (_instance == null)
                {
                    CreateInstance();
                }
                return _instance;
            }
        }

        public static bool IsValidInstance => _instance != null && !_isDestroyed;

        private static T _instance;
        private static bool _isDestroyed = false;

        protected virtual void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this as T;
            _isDestroyed = false;
            DontDestroyOnLoad(gameObject);
        }

        protected virtual void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
                _isDestroyed = true;
            }
        }

        private static void CreateInstance()
        {
            if (_instance != null)
                return;

            var go = new GameObject(typeof(T).Name);
            _instance = go.AddComponent<T>();
            DontDestroyOnLoad(go);
        }
    }

    /// <summary>
    /// 씬별 싱글톤 (씬 전환 시 자동 해제)
    /// </summary>
    public abstract class MonoSceneSingleton<T> : MonoBehaviour where T : MonoSceneSingleton<T>
    {
        public static T Instance
        {
            get
            {
                if (_instance == null)
                {
                    CreateInstance();
                }
                return _instance;
            }
        }

        public static bool IsValidInstance => _instance != null;

        private static T _instance;

        protected virtual void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this as T;
        }

        protected virtual void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private static void CreateInstance()
        {
            if (_instance != null)
                return;

            var go = new GameObject(typeof(T).Name);
            _instance = go.AddComponent<T>();
        }
    }
}

