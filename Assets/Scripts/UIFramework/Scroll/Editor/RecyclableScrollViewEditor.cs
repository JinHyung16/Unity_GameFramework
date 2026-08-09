using UnityEditor;
using UnityEditor.UI;

namespace Game_UIFramework
{
    [CustomEditor(typeof(RecyclableScrollView), true)]
    [CanEditMultipleObjects]
    public class RecyclableScrollViewEditor : ScrollRectEditor
    {
        private SerializedProperty _cellSizeMode;
        private SerializedProperty _cellPrefab;
        private SerializedProperty _cellSize;
        private SerializedProperty _spacing;
        private SerializedProperty _padding;
        private SerializedProperty _reverseArrangement;
        private SerializedProperty _minPoolCount;
        private SerializedProperty _offscreenBuffer;

        protected override void OnEnable()
        {
            base.OnEnable();
            _cellSizeMode = serializedObject.FindProperty("_cellSizeMode");
            _cellPrefab = serializedObject.FindProperty("_cellPrefab");
            _cellSize = serializedObject.FindProperty("_cellSize");
            _spacing = serializedObject.FindProperty("_spacing");
            _padding = serializedObject.FindProperty("_padding");
            _reverseArrangement = serializedObject.FindProperty("_reverseArrangement");
            _minPoolCount = serializedObject.FindProperty("_minPoolCount");
            _offscreenBuffer = serializedObject.FindProperty("_offscreenBuffer");

            EditorApplication.hierarchyChanged += RefreshTargets;
            RefreshTargets();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            EditorApplication.hierarchyChanged -= RefreshTargets;
        }

        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();

            base.OnInspectorGUI();

            serializedObject.Update();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Recyclable Scroll", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_cellPrefab);
            EditorGUILayout.PropertyField(_cellSizeMode);
            if (_cellSizeMode.enumValueIndex == (int)CellSizeModeType.Static)
                EditorGUILayout.PropertyField(_cellSize);
            EditorGUILayout.PropertyField(_spacing);
            EditorGUILayout.PropertyField(_padding);
            EditorGUILayout.PropertyField(_reverseArrangement);
            EditorGUILayout.PropertyField(_minPoolCount);
            EditorGUILayout.PropertyField(_offscreenBuffer);
            serializedObject.ApplyModifiedProperties();

            if (EditorGUI.EndChangeCheck())
                RefreshTargets();
        }

        private void RefreshTargets()
        {
            foreach (var target in targets)
            {
                if (target is RecyclableScrollView view)
                {
                    view.RefreshEditorPreview();
                    EditorUtility.SetDirty(view);
                }
            }
        }
    }
}
