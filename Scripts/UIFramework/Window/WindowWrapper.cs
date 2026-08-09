namespace Game_UIFramework
{
    /// <summary>
    /// 윈도우 래퍼 클래스 - 윈도우와 관련 정보를 함께 저장
    /// </summary>
    public class WindowWrapper
    {
        public BaseWindow Window { get; set; }
        public WindowType WindowType { get; set; }
        public int Layer { get; set; }
    }
}



