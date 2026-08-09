using UnityEngine;
using UnityEngine.UI;

namespace Game_UIFramework
{
    /// <summary>
    /// 모든 UI 컴포넌트의 기본 클래스
    /// Transform과 RectTransform을 캐싱하여 성능 최적화
    /// </summary>
    public class BaseComponent : MonoBehaviour
    {
        private Transform _cachedTransform;
        private RectTransform _cachedRectTransform;
        private bool _initTransform;
        private bool _initRectTransform;

        /// <summary>
        /// 캐싱된 Transform 컴포넌트
        /// </summary>
        public Transform CachedTransform
        {
            get
            {
                if (!_initTransform)
                {
                    _cachedTransform = transform;
                    _initTransform = true;
                }
                return _cachedTransform;
            }
        }

        /// <summary>
        /// 캐싱된 RectTransform 컴포넌트
        /// </summary>
        public RectTransform RectTransform
        {
            get
            {
                if (!_initRectTransform)
                {
                    _cachedRectTransform = GetComponent<RectTransform>();
                    _initRectTransform = true;
                }
                return _cachedRectTransform;
            }
        }

        protected virtual void Awake()
        {
        }

        protected virtual void OnDestroy()
        {
        }
    }
}



