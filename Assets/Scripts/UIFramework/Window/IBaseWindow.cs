namespace Game_UIFramework
{
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
