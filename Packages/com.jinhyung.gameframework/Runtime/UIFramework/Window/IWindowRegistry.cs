namespace Game_UIFramework
{
    public interface IWindowRegistry
    {
        void AddWindow<T>(WindowKey<T> key, WindowType windowType = WindowType.Normal) where T : BaseWindow;

        void RegisterWindow<T>(WindowKey<T> key, WindowType windowType = WindowType.Normal) where T : BaseWindow;
    }
}
