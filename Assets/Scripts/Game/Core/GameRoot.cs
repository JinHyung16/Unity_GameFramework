namespace Game_Core
{
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
