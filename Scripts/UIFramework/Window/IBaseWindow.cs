namespace Game_UIFramework
{
    /// <summary>
    /// 윈도우 기본 인터페이스
    /// </summary>
    public interface IBaseWindow
    {
        bool IsOpen();
        void Close();
        void ForcedClose();
        int GetDepth();
        string GetName();
        WindowType GetWindowType();
        WindowStateType GetWindowState();
    }
}



