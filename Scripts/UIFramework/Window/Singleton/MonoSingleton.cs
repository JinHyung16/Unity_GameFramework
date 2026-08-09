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

    /// <summary>
    /// 씬별 싱글톤 (씬 전환 시 자동 해제)
    /// </summary>
    public abstract class MonoSceneSingleton<T> : MonoBehaviour where T : MonoSceneSingleton<T>
    {
        public static T Instance
        {
            get
            {
                // 종료 중에는 새로 만들지 않는다. 다른 객체의 OnDestroy에서 Instance를 건드리면
                // 파괴 직후에 유령 GameObject가 하나 더 생기기 때문이다.
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
            // 씬을 다시 열면 정상 동작해야 하므로 플래그는 여기서 되돌린다.
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

