namespace Game_UIFramework
{
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
