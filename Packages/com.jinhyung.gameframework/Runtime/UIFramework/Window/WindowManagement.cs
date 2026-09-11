using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game_UIFramework
{
    public class WindowManagement
        : MonoSceneSingleton<WindowManagement>
        , IWindowObserver
    {
        private Dictionary<WindowKey, WindowWrapper> _windows = new Dictionary<WindowKey, WindowWrapper>(WindowKeyEqualityComparer.Default);
        private Transform _uiRoot;
        private Camera _uiCamera;

        public void SetUIEnvironment(Transform uiRoot, Camera uiCamera)
        {
            if (uiRoot != null)
            {
                _uiRoot = uiRoot;
                WarnIfRootHasCanvas(_uiRoot);
            }
            _uiCamera = uiCamera;
        }

        private static void WarnIfRootHasCanvas(Transform root)
        {
            if (root == null)
            {
                return;
            }

            var parentCanvas = root.GetComponentInParent<Canvas>();
            if (parentCanvas == null)
            {
                return;
            }

            Debug.LogWarning(
                $"[UIFramework] UIRoot '{root.name}' 위에 Canvas 가 있습니다 ('{parentCanvas.name}'). " +
                "창 Canvas 가 중첩되어 깊이(sortingOrder) 가 적용되지 않습니다. " +
                "UIRoot 는 Canvas 없는 빈 GameObject 로 두세요.");
        }

        private Dictionary<WindowType, DepthInfo> _depthInfos = new Dictionary<WindowType, DepthInfo>();
        private readonly Dictionary<WindowType, List<BaseWindow>> _openedByType = new Dictionary<WindowType, List<BaseWindow>>();

        private readonly List<IWindowUpdate> _updateWindows = new List<IWindowUpdate>();

        private readonly List<IWindowUpdate> _updateBuffer = new List<IWindowUpdate>();

        private bool _tearingDown;

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
            _tearingDown = true;
            try
            {
                RestoreAllWindowsInternal();
            }
            finally
            {
                _tearingDown = false;
            }
        }

        private void RestoreAllWindowsInternal()
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

            _openedByType.Clear();
            _updateWindows.Clear();
            _updateBuffer.Clear();

            _depthInfos.Clear();

            base.OnDestroy();
        }

        private void Update()
        {
            if (_updateWindows.Count == 0)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            _updateBuffer.Clear();
            _updateBuffer.AddRange(_updateWindows);

            for (int i = 0; i < _updateBuffer.Count; i++)
            {
                _updateBuffer[i]?.OnUpdate(deltaTime);
            }
        }

        private void FixedUpdate()
        {
            if (_updateWindows.Count == 0)
            {
                return;
            }

            float fixedDeltaTime = Time.fixedDeltaTime;
            _updateBuffer.Clear();
            _updateBuffer.AddRange(_updateWindows);

            for (int i = 0; i < _updateBuffer.Count; i++)
            {
                _updateBuffer[i]?.OnFixedUpdate(fixedDeltaTime);
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

        private void HandleOpenWindow(BaseWindow window)
        {
            if (window == null)
                return;

            var windowType = window.GetWindowType();
            var windowList = GetWindowList(windowType);
            if (!windowList.Contains(window))
            {
                var covered = windowList.Count > 0 ? windowList[windowList.Count - 1] : null;
                windowList.Add(window);

                if (covered != null && _tearingDown == false)
                {
                    covered.OtherWindowOpened();
                }
            }
            ReassignDepths(windowType);

            if (window is IWindowUpdate updatable && !_updateWindows.Contains(updatable))
            {
                _updateWindows.Add(updatable);
            }
        }

        private void HandleCloseWindow(BaseWindow window)
        {
            if (window == null)
                return;

            var windowType = window.GetWindowType();
            var windowList = GetWindowList(windowType);

            bool wasTop = windowList.Count > 0 && windowList[windowList.Count - 1] == window;
            windowList.Remove(window);
            ReassignDepths(windowType);

            if (wasTop && windowList.Count > 0 && _tearingDown == false)
            {
                windowList[windowList.Count - 1].ReOpened();
            }

            if (window is IWindowUpdate updatable)
            {
                _updateWindows.Remove(updatable);
            }
        }

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

        private void InitializeDepthInfo()
        {
            _depthInfos[WindowType.Normal] = new DepthInfo { MinDepth = 100, DepthInterval = 10 };
            _depthInfos[WindowType.Popup] = new DepthInfo { MinDepth = 200, DepthInterval = 10 };
            _depthInfos[WindowType.HUD] = new DepthInfo() { MinDepth = 10, DepthInterval = 10 };
            _depthInfos[WindowType.Modal] = new DepthInfo { MinDepth = 400, DepthInterval = 10 };
            _depthInfos[WindowType.GlobalPopup] = new DepthInfo { MinDepth = 500, DepthInterval = 10 };
        }

        private List<BaseWindow> GetWindowList(WindowType windowType)
        {
            if (_openedByType.TryGetValue(windowType, out var list) == false)
            {
                list = new List<BaseWindow>();
                _openedByType[windowType] = list;
            }
            return list;
        }

        private DepthInfo GetDepthInfo(WindowType windowType)
        {
            _depthInfos.TryGetValue(windowType, out var depthInfo);
            return depthInfo;
        }

        public bool IsAnyWindowOpen()
        {
            foreach (var pair in _openedByType)
            {
                if (pair.Value.Count > 0)
                    return true;
            }
            return false;
        }

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
                WarnIfRootHasCanvas(_uiRoot);
            }
        }
    }
}
