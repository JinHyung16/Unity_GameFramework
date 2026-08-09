using Newtonsoft.Json;

namespace Game_DataLoader
{
    /// <summary>
    /// 데이터 테이블 역직렬화 공용 설정.
    /// exporter가 뽑는 배열-오브젝트 JSON을 List&lt;TValue&gt;로 읽을 때 사용한다.
    /// </summary>
    public static class JsonSettings
    {
        public static readonly JsonSerializerSettings Default = new JsonSerializerSettings
        {
            // 값이 null이면 프로퍼티를 건드리지 않고 기본값 유지
            NullValueHandling = NullValueHandling.Ignore,
            // JSON에 있지만 클래스에 없는 컬럼은 무시 (컬럼 추가/제거 내성)
            MissingMemberHandling = MissingMemberHandling.Ignore,
        };
    }
}
