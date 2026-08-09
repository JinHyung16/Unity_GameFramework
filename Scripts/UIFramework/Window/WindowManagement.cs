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
        
        // 깊이 관리 — 타입별로 "열린 순서" 리스트를 들고 있고, 열고 닫을 때마다 그 리스트 순서대로
        // sortingOrder를 다시 매긴다. 카운터만 증감하면 중간 창이 닫힐 때 깊이가 겹친다.
        private Dictionary<WindowType, DepthInfo> _depthInfos = new Dictionary<WindowType, DepthInfo>();
        private readonly Dictionary<WindowType, List<BaseWindow>> _openedByType = new Dictionary<WindowType, List<BaseWindow>>();

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
            _openedByType.Clear();
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
            var windowList = GetWindowList(windowType);
            if (!windowList.Contains(window))
            {
                windowList.Add(window);
            }
            ReassignDepths(windowType);

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
            var windowList = GetWindowList(windowType);
            windowList.Remove(window);
            ReassignDepths(windowType);

            if (window is IWindowUpdate updatable)
            {
                _updateWindows.Remove(updatable);
            }
        }

        /// <summary>
        /// 해당 타입에서 열려 있는 창들에 MinDepth부터 간격만큼 깊이를 다시 매긴다.
        /// 중간 창이 닫혀도 남은 창들의 깊이가 겹치지 않고 촘촘하게 유지된다.
        /// </summary>
        private void ReassignDepths(WindowType windowType)
        {
            var depthInfo = GetDepthInfo(windowType);
            if (depthInfo == null)
                return;

            var windowList = GetWindowList(windowType);
            for (int i = 0; i < windowList.Count; i++)
            {
                windowList[i]?.SetDepth(depthInfo.MinDepth + i * depthInfo.DepthInterval);
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
        /// 윈도우 타입별 "열린 순서" 리스트 가져오기 (없으면 만든다)
        /// </summary>
        private List<BaseWindow> GetWindowList(WindowType windowType)
        {
            if (_openedByType.TryGetValue(windowType, out var list) == false)
            {
                list = new List<BaseWindow>();
                _openedByType[windowType] = list;
            }
            return list;
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
            foreach (var pair in _openedByType)
            {
                if (pair.Value.Count > 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 깊이 정보 클래스 (타입별 시작 깊이와 창 사이 간격)
        /// </summary>
        private class DepthInfo
        {
            public int MinDepth { get; set; }
            public int DepthInterval { get; set; }
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

