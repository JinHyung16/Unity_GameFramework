namespace Game_UIFramework
{
    public interface IRecyclableScrollDataSource
    {
        int GetItemCount();

        void CreateCell(IRecyclableItem cell, int index);
    }
}
