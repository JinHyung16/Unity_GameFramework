using System;

namespace Game_UIFramework
{
    public interface IWindowController
    {
        T GetWindow<T>(WindowKey<T> key, bool createIfNotExists = true) where T : BaseWindow;

        T OpenWindow<T>(WindowKey<T> key, Action<T> onOpenBefore = null, Action<T> onOpenAfter = null) where T : BaseWindow;

        void CloseWindow<T>(WindowKey<T> key) where T : BaseWindow;

        void ForceCloseWindow<T>(WindowKey<T> key) where T : BaseWindow;

        bool IsWindowOpen<T>(WindowKey<T> key) where T : BaseWindow;
    }
}
