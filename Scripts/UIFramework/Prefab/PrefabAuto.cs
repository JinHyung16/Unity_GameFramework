using Game_Utility;
using UnityEngine;

namespace Game_UIFramework
{
    /// <summary>
    /// PrefabAuto 키 팩토리
    /// WindowKey처럼 각 컴포넌트 클래스에 static readonly로 선언해둔다
    /// 예) public static readonly PrefabAuto&lt;CurrencyComponent&gt; Auto = PrefabAuto.Get&lt;CurrencyComponent&gt;("UI/Components/CurrencyComponent");
    /// </summary>
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

    /// <summary>
    /// BaseWindow가 아닌 BaseComponent용 프리팹 키
    /// Create로 풀에서 꺼내고 Release로 풀에 반환한다 (WindowKey와 동일한 관리 방식)
    /// </summary>
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

        /// <summary>
        /// UI 프리팹만 사용 해주세요.
        /// </summary>
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

        /// <summary>
        /// 사용이 끝난 인스턴스를 풀에 반환
        /// </summary>
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
