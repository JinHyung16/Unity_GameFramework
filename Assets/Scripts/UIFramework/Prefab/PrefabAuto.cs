using Game_Utility;
using UnityEngine;

namespace Game_UIFramework
{
    public static class PrefabAuto
    {
        public static PrefabAuto<T> Get<T>(string path) where T : BaseComponent
        {
            return new PrefabAuto<T>(path);
        }

        public static PrefabAuto<T> GetGlobal<T>(string path) where T : BaseComponent
        {
            return new PrefabAuto<T>(path, true);
        }
    }

    public class PrefabAuto<T>
        where T : BaseComponent
    {
        public string Path;
        public bool IsGlobal;

        public PrefabAuto(string path)
        {
            Path = path;
        }

        public PrefabAuto(string path, bool isGlobal)
        {
            Path = path;
            IsGlobal = isGlobal;
        }

        public T Create(Transform transform, bool isActive = true)
        {
            var comp = IsGlobal == false ? PrefabLoader.Get<T>(Path) : PrefabLoader.GetGlobal<T>(Path);
            if (comp == null)
            {
                return null;
            }

            if (transform != null)
            {
                comp.CachedTransform.SetParent(transform, false);
                comp.CachedTransform.ResetLocalTM();
            }

            comp.gameObject.SetActive(isActive);

            return comp;
        }

        public T CreateForUI(Transform transform, bool isActive = true)
        {
            var comp = IsGlobal == false ? PrefabLoader.Get<T>(Path) : PrefabLoader.GetGlobal<T>(Path);
            if (comp == null)
            {
                return null;
            }

            if (transform != null)
            {
                comp.RectTransform.SetParent(transform, false);
                comp.RectTransform.ResetAnchoredPos();
            }

            comp.gameObject.SetActive(isActive);

            return comp;
        }

        public void Release(T comp)
        {
            PrefabLoader.Release(comp);
        }

        public void Preload(int count)
        {
            PrefabLoader.Preload<T>(Path, count, IsGlobal);
        }
    }
}
