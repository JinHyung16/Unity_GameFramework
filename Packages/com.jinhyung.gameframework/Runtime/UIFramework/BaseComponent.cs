using UnityEngine;
using UnityEngine.UI;

namespace Game_UIFramework
{
    public class BaseComponent : MonoBehaviour
    {
        private Transform _cachedTransform;
        private RectTransform _cachedRectTransform;
        private bool _initTransform;
        private bool _initRectTransform;

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
