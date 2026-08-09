namespace Game_UIFramework
{
    /// <summary>
    /// PrefabAuto 풀 수명 콜백
    /// 풀에서 꺼낼 때 OnSpawn, 반환될 때 OnDespawn이 호출된다
    /// 이벤트 구독 해제/상태 초기화는 OnDespawn에서 처리한다
    /// </summary>
    public interface IPoolable
    {
        void OnSpawn();
        void OnDespawn();
    }
}
