namespace Game_UIFramework
{
    public interface IWindowObserver
    {
        void OnWindowStateChanged(BaseWindow window, WindowStateType state);
    }
}
