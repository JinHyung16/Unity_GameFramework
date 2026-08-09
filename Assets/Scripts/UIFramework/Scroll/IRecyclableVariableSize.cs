namespace Game_UIFramework
{
    /// <summary>
    /// 항목마다 길이가 다른 리스트용 데이터 소스 보조 인터페이스.
    /// RecyclableScrollView의 CellSizeMode가 PerItem일 때만 쓰인다.
    /// 원본 웹 레이아웃처럼 내용 크기로 행 높이가 갈리는 화면을 그대로 재현할 때 필요하다.
    /// </summary>
    public interface IRecyclableVariableSize
    {
        /// <summary>세로 스크롤이면 높이, 가로면 너비 (캔버스 단위)</summary>
        float GetItemLength(int index);
    }
}
