namespace Game_Core
{
    /// <summary>
    /// 데이터 컨테이너 접근 루트
    /// GameRoot.Instance.XxxDataContainer.Get(key) 형태로 정적 데이터를 조회한다
    /// 컨테이너 프로퍼티는 run_win.bat(smart_exporter)이 GameRoot.Generated.cs에 자동 생성
    /// </summary>
    public partial class GameRoot
    {
        public static GameRoot Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new GameRoot();
                }
                return _instance;
            }
        }

        private static GameRoot _instance;

        private GameRoot()
        {
        }
    }
}
