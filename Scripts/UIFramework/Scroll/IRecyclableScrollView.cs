using UnityEngine;

namespace Game_UIFramework
{
    public interface IRecyclableScrollView
    {
        void Initialize(IRecyclableScrollDataSource dataSource, GameObject cellPrefab = null);

        void Reload();

        void RefreshVisibleCells();

        void ScrollToIndex(int index);
    }
}
