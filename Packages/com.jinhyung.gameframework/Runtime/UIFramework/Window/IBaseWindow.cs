namespace Game_UIFramework
{
    public interface IBaseWindow
    {
        bool IsOpen();
        void Close();
        void ForcedClose();
        void OtherWindowOpened();
        void ReOpened();
        int GetDepth();
        string GetName();
        WindowType GetWindowType();
        WindowStateType GetWindowState();
    }
}
