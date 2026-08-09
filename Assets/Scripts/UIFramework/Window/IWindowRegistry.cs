namespace Game_UIFramework
{
    /// <summary>
    /// 윈도우 등록 인터페이스
    /// 윈도우 등록만 담당하는 역할 분리
    /// </summary>
    public interface IWindowRegistry
    {
        /// <summary>
        /// 윈도우 등록
        /// </summary>
        void AddWindow<T>(WindowKey<T> key, WindowType windowType = WindowType.Normal) where T : BaseWindow;

        /// <summary>
        /// 윈도우 등록 (별칭)
        /// </summary>
        void RegisterWindow<T>(WindowKey<T> key, WindowType windowType = WindowType.Normal) where T : BaseWindow;
    }
}

