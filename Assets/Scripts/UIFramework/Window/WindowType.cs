namespace Game_UIFramework
{
    /// <summary>
    /// 윈도우 타입 열거형
    /// </summary>
    public enum WindowType
    {
        Normal = 0, // 기본
        Popup = 1, // 기본 UI들 상단 팝업
        HUD = 2, // 항상 열려있는 UI
        GlobalPopup = 3, // 가장 최상단 위 Popup
        Modal = 4, // Popup 위, GlobalPopup 아래 (확인/차단형)
    }
}



