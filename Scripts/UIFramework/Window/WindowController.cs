using System;

namespace Game_UIFramework
{
    /// <summary>
    /// 윈도우 제어 클래스
    /// WindowManagement를 래핑하여 윈도우 제어 기능만 제공
    /// </summary>
    public class WindowController : IWindowController
    {
        private readonly WindowManagement _windowManagement;

        public WindowController(WindowManagement windowManagement)
        {
            _windowManagement = windowManagement;
        }

        public T GetWindow<T>(WindowKey<T> key, bool createIfNotExists = true) where T : BaseWindow
        {
            return _windowManagement?.GetWindow(key, createIfNotExists);
        }

        public T OpenWindow<T>(WindowKey<T> key, Action<T> onOpenBefore = null, Action<T> onOpenAfter = null) where T : BaseWindow
        {
            return _windowManagement?.OpenWindow(key, onOpenBefore, onOpenAfter);
        }

        public void CloseWindow<T>(WindowKey<T> key) where T : BaseWindow
        {
            _windowManagement?.CloseWindow(key);
        }

        public void ForceCloseWindow<T>(WindowKey<T> key) where T : BaseWindow
        {
            _windowManagement?.ForceCloseWindow(key);
        }

        public bool IsWindowOpen<T>(WindowKey<T> key) where T : BaseWindow
        {
            return _windowManagement != null && _windowManagement.IsWindowOpen(key);
        }
    }
}

