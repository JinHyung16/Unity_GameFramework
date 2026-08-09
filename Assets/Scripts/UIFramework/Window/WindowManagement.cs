using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game_UIFramework
{
    /// <summary>
    /// 윈도우 관리 클래스 (씬별 싱글톤)
    /// 윈도우의 생성, 열기, 닫기, 상태 관리, 깊이 관리를 담당
    /// </summary>
    public class WindowManagement 
        : MonoSceneSingleton<WindowManagement>
        , IWindowObserver
    {
        private Dictionary<WindowKey, WindowWrapper> _windows = new Dictionary<WindowKey, WindowWrapper>(WindowKeyEqualityComparer.Default);
        private Transform _uiRoot;
        private Camera _uiCamera;

        /// <summary>
        /// UI 환경 바인딩 (게임 초기화 시 호출)
        /// 이후 생성되는 모든 윈도우는 uiRoot 아래에 붙고 uiCamera로 렌더링된다
        /// </summary>
        public void SetUIEnvironment(Transform uiRoot, Camera uiCamera)
        {
            if (uiRoot != null)
            {
                _uiRoot = uiRoot;
            }
            _uiCamera = uiCamera;
        }
        
        // 깊이 관리
        private Dictionary<WindowType, DepthInfo> _depthInfos = new Dictionary<WindowType, DepthInfo>();
        private List<IBaseWindow> _openedNormalWindows = new List<IBaseWindow>();
        private List<IBaseWindow> _openedPopupWindows = new List<IBaseWindow>();

        private readonly List<IWindowUpdate> _updateWindows = new List<IWindowUpdate>();

        public void AddWindow<T>(WindowKey<T> key, WindowType windowType = WindowType.Normal) where T : BaseWindow
        {
            if (_windows.ContainsKey(key))
                return;

            _windows[key] = new WindowWrapper
            {
                WindowType = windowType
            };
        }

        public void RegisterWindow<T>(WindowKey<T> key, WindowType windowType = WindowType.Normal) where T : BaseWindow
        {
            AddWindow(key, windowType);
        }

        public T GetWindow<T>(WindowKey<T> key, bool createIfNotExists = true) where T : BaseWindow
        {
            if (!_windows.TryGetValue(key, out var wrapper))
            {
                if (!createIfNotExists)
                    return null;

                RegisterWindow(key, WindowType.Normal);
                wrapper = _windows[key];
            }

            if (wrapper.Window == null && createIfNotExists)
            {
                if (_uiRoot == null)
                {
                    InitializeUIRoot();
                }
                var window = WindowFactory.LoadWindow<T>(key, _uiRoot);
                if (window != null)
                {
                    window.WindowType = wrapper.WindowType;
                    window.BindCamera(_uiCamera);
                    window.AddObserver(this);
                    wrapper.Window = window;
                }
            }

            return wrapper?.Window as T;
        }

        public T OpenWindow<T>(WindowKey<T> key, Action<T> onOpenBefore = null, Action<T> onOpenAfter = null) where T : BaseWindow
        {
            var window = GetWindow(key);
            if (window == null)
                return null;

            if (window.IsOpen())
                return window;

            window.OpenInternal(
                () => onOpenBefore?.Invoke(window),
                () => onOpenAfter?.Invoke(window)
            );

            return window;
        }

        public void CloseWindow<T>(WindowKey<T> key) where T : BaseWindow
        {
            var window = GetWindow(key, false);
            if (window != null && window.IsOpen())
            {
                window.Close();
            }
        }

        public void ForceCloseWindow<T>(WindowKey<T> key) where T : BaseWindow
        {
            var window = GetWindow(key, false);
            if (window != null && window.IsOpen())
            {
                window.ForcedClose();
            }
        }

        public bool IsWindowOpen<T>(WindowKey<T> key) where T : BaseWindow
        {
            var window = GetWindow(key, false);
            return window != null && window.IsOpen();
        }

        public void CloseAllWindows(bool forced = false)
        {
            foreach (var pair in _windows)
            {
                var window = pair.Value.Window;
                if (window != null && window.IsOpen())
                {
                    if (forced)
                        window.ForcedClose();
                    else
                        window.Close();
                }
            }
        }

        public void RestoreAllWindows()
        {
            foreach (var pair in _windows)
            {
                var window = pair.Value.Window;
                if (window != null)
                {
                    window.ForcedClose();
                    window.RemoveObserver(this);
                    Destroy(window.gameObject);
                }
                pair.Value.Window = null;
            }
        }

        protected override void Awake()
        {
            base.Awake();
            InitializeDepthInfo();
        }

        protected override void OnDestroy()
        {
            RestoreAllWindows();
            _windows.Clear();
            
            // 열린 윈도우 리스트 정리
            _openedNormalWindows.Clear();
            _openedPopupWindows.Clear();
            _updateWindows.Clear();
            
            // 깊이 정보 초기화
            _depthInfos.Clear();
            
            base.OnDestroy();
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            for (int i = _updateWindows.Count - 1; i >= 0; i--)
            {
                if (i >= _updateWindows.Count)
                {
                    continue;
                }
                _updateWindows[i]?.OnUpdate(deltaTime);
            }
        }

        private void FixedUpdate()
        {
            float fixedDeltaTime = Time.fixedDeltaTime;
            for (int i = _updateWindows.Count - 1; i >= 0; i--)
            {
                if (i >= _updateWindows.Count)
                {
                    continue;
                }
                _updateWindows[i]?.OnFixedUpdate(fixedDeltaTime);
            }
        }

        void IWindowObserver.OnWindowStateChanged(BaseWindow window, WindowStateType state)
        {
            if (window == null)
                return;

            switch (state)
            {
                case WindowStateType.Opening:
                    HandleOpenWindow(window);
                    break;

                case WindowStateType.Closed:
                    HandleCloseWindow(window);
                    break;
            }
        }
        
        /// <summary>
        /// 윈도우 열기 처리 (깊이 관리)
        /// </summary>
        private void HandleOpenWindow(BaseWindow window)
        {
            if (window == null)
                return;

            var windowType = window.GetWindowType();
            var depthInfo = GetDepthInfo(windowType);
            
            if (depthInfo != null)
            {
                var depth = depthInfo.GetNextDepth();
                window.SetDepth(depth);
            }

            var windowList = GetWindowList(windowType);
            windowList.Add(window);

            if (window is IWindowUpdate updatable && !_updateWindows.Contains(updatable))
            {
                _updateWindows.Add(updatable);
            }
        }

        /// <summary>
        /// 윈도우 닫기 처리 (깊이 관리)
        /// </summary>
        private void HandleCloseWindow(BaseWindow window)
        {
            if (window == null)
                return;

            var windowType = window.GetWindowType();
            var depthInfo = GetDepthInfo(windowType);
            
            if (depthInfo != null)
            {
                depthInfo.DecreaseWindow();
            }

            var windowList = GetWindowList(windowType);
            windowList.Remove(window);

            if (window is IWindowUpdate updatable)
            {
                _updateWindows.Remove(updatable);
            }
        }
        
        /// <summary>
        /// 깊이 정보 초기화
        /// </summary>
        private void InitializeDepthInfo()
        {
            _depthInfos[WindowType.Normal] = new DepthInfo { MinDepth = 100, DepthInterval = 10 };
            _depthInfos[WindowType.Popup] = new DepthInfo { MinDepth = 200, DepthInterval = 10 };
            _depthInfos[WindowType.HUD] = new DepthInfo() { MinDepth = 10, DepthInterval = 10 };
            _depthInfos[WindowType.Modal] = new DepthInfo { MinDepth = 400, DepthInterval = 10 };
            _depthInfos[WindowType.GlobalPopup] = new DepthInfo { MinDepth = 500, DepthInterval = 10 };
        }
        
        /// <summary>
        /// 윈도우 타입별 리스트 가져오기
        /// </summary>
        private List<IBaseWindow> GetWindowList(WindowType windowType)
        {
            switch (windowType)
            {
                case WindowType.Popup:
                case WindowType.Modal:
                    return _openedPopupWindows;
                default:
                    return _openedNormalWindows;
            }
        }
        
        /// <summary>
        /// 깊이 정보 가져오기
        /// </summary>
        private DepthInfo GetDepthInfo(WindowType windowType)
        {
            _depthInfos.TryGetValue(windowType, out var depthInfo);
            return depthInfo;
        }
        
        /// <summary>
        /// 열린 윈도우가 있는지 확인
        /// </summary>
        public bool IsAnyWindowOpen()
        {
            return _openedNormalWindows.Count > 0 || _openedPopupWindows.Count > 0;
        }
        
        /// <summary>
        /// 깊이 정보 클래스
        /// </summary>
        private class DepthInfo
        {
            public int MinDepth { get; set; }
            public int DepthInterval { get; set; }
            private int _windowCount = 0;

            public int GetNextDepth()
            {
                _windowCount++;
                return MinDepth + (_windowCount - 1) * DepthInterval;
            }

            public void DecreaseWindow()
            {
                if (_windowCount > 0)
                    _windowCount--;
            }
        }

        private void InitializeUIRoot()
        {
            if (_uiRoot == null)
            {
                var rootGo = GameObject.Find("UI_Root");
                if (rootGo == null)
                {
                    rootGo = GameObject.Find("UIRoot");
                }
                if (rootGo == null)
                {
                    rootGo = new GameObject("UIRoot");
                }
                _uiRoot = rootGo.transform;
            }
        }
    }
}

