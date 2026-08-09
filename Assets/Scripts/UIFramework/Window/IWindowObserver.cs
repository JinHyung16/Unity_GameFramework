namespace Game_UIFramework
{
    /// <summary>
    /// 윈도우 상태 변경을 관찰하는 옵저버 인터페이스
    /// </summary>
    public interface IWindowObserver
    {
        void OnWindowStateChanged(BaseWindow window, WindowStateType state);
    }
}



