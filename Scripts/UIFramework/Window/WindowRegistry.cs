namespace Game_UIFramework
{
    /// <summary>
    /// 윈도우 등록 클래스
    /// WindowManagement를 래핑하여 윈도우 등록 기능만 제공
    /// </summary>
    public class WindowRegistry : IWindowRegistry
    {
        private readonly WindowManagement _windowManagement;

        public WindowRegistry(WindowManagement windowManagement)
        {
            _windowManagement = windowManagement;
        }

        public void AddWindow<T>(WindowKey<T> key, WindowType windowType = WindowType.Normal) where T : BaseWindow
        {
            _windowManagement?.AddWindow(key, windowType);
        }

        public void RegisterWindow<T>(WindowKey<T> key, WindowType windowType = WindowType.Normal) where T : BaseWindow
        {
            AddWindow(key, windowType);
        }
    }
}

