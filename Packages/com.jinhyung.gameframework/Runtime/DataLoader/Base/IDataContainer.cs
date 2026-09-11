namespace Game_DataLoader
{
    public interface IDataContainer
    {
        string Name { get; }
        bool Loaded { get; }
        void LoadJson(string text);
        void Clear();
        bool Validate(out string errorMessage);
        void AfterAllTableLoaded();
    }
}
