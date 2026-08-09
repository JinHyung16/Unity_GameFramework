using System;

namespace Game_UIFramework
{
    /// <summary>
    /// 윈도우 제어 인터페이스
    /// 윈도우 Open/Close 처리만 담당하는 역할 분리
    /// </summary>
    public interface IWindowController
    {
        /// <summary>
        /// 윈도우 가져오기
        /// </summary>
        T GetWindow<T>(WindowKey<T> key, bool createIfNotExists = true) where T : BaseWindow;

        /// <summary>
        /// 윈도우 열기
        /// </summary>
        T OpenWindow<T>(WindowKey<T> key, Action<T> onOpenBefore = null, Action<T> onOpenAfter = null) where T : BaseWindow;

        /// <summary>
        /// 윈도우 닫기
        /// </summary>
        void CloseWindow<T>(WindowKey<T> key) where T : BaseWindow;

        /// <summary>
        /// 윈도우 강제 닫기
        /// </summary>
        void ForceCloseWindow<T>(WindowKey<T> key) where T : BaseWindow;

        /// <summary>
        /// 윈도우가 열려있는지 확인
        /// </summary>
        bool IsWindowOpen<T>(WindowKey<T> key) where T : BaseWindow;
    }
}

