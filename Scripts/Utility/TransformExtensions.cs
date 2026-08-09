using UnityEngine;

namespace Game_Utility
{
    /// <summary>
    /// Transform / RectTransform 리셋 확장
    /// </summary>
    public static class TransformExtensions
    {
        /// <summary>
        /// 로컬 TM(위치/회전/스케일)을 초기값으로 리셋
        /// </summary>
        public static void ResetLocalTM(this Transform transform)
        {
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
        }

        /// <summary>
        /// UI용 리셋 - 앵커 위치/회전/스케일을 초기값으로 리셋
        /// </summary>
        public static void ResetAnchoredPos(this RectTransform rectTransform)
        {
            rectTransform.anchoredPosition3D = Vector3.zero;
            rectTransform.localRotation = Quaternion.identity;
            rectTransform.localScale = Vector3.one;
        }
    }
}
