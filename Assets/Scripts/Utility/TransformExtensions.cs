using UnityEngine;

namespace Game_Utility
{
    public static class TransformExtensions
    {
        public static void ResetLocalTM(this Transform transform)
        {
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
        }

        public static void ResetAnchoredPos(this RectTransform rectTransform)
        {
            rectTransform.anchoredPosition3D = Vector3.zero;
            rectTransform.localRotation = Quaternion.identity;
            rectTransform.localScale = Vector3.one;
        }
    }
}
