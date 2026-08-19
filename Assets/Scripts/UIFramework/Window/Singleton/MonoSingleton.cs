using UnityEngine;

namespace Game_UIFramework
{
    public abstract class MonoSingleton<T> : MonoBehaviour where T : MonoSingleton<T>
    {
        public static T Instance
        {
            get
            {
                if (_isDestroyed || _isQuitting)
                    return null;

                if (_instance == null)
                {
                    CreateInstance();
                }
                return _instance;
            }
        }

        public static bool IsValidInstance => _instance != null && !_isDestroyed && !_isQuitting;

        private static T _instance;
        private static bool _isDestroyed = false;
        private static bool _isQuitting = false;

        protected virtual void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this as T;
            _isDestroyed = false;
            _isQuitting = false;
            DontDestroyOnLoad(gameObject);
        }

        protected virtual void OnApplicationQuit()
        {
            _isQuitting = true;
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

    public abstract class MonoSceneSingleton<T> : MonoBehaviour where T : MonoSceneSingleton<T>
    {
        public static T Instance
        {
            get
            {
                if (_isQuitting)
                    return null;

                if (_instance == null)
                {
                    CreateInstance();
                }
                return _instance;
            }
        }

        public static bool IsValidInstance => _instance != null && !_isQuitting;

        private static T _instance;
        private static bool _isQuitting = false;

        protected virtual void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this as T;

            _isQuitting = false;
        }

        protected virtual void OnApplicationQuit()
        {
            _isQuitting = true;
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
