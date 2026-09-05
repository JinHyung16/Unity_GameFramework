using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game_DataLoader
{
    /// <summary>
    /// _DataExporter/config.json 의 "Paths" 를 편집한다.
    /// Node 익스포터와 DB Generate 가 같은 파일을 읽으므로, 여기서 바꾸면 양쪽에 함께 반영된다.
    /// </summary>
    public class DataPipelineSettingsWindow : EditorWindow
    {
        private DataPipelinePaths _paths;
        private Vector2 _scroll;
        private bool _dirty;

        [MenuItem("Tools/GameData/Settings", false, 11)]
        private static void Open()
        {
            var window = GetWindow<DataPipelineSettingsWindow>("GameData Settings");
            window.minSize = new Vector2(560f, 340f);
        }

        private void OnEnable()
        {
            Reload();
        }

        private void Reload()
        {
            _paths = DataPipelineConfig.Load();
            _dirty = false;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("데이터 파이프라인 경로", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(DataPipelineConfig.ConfigRelativePath, EditorStyles.miniLabel);

            if (DataPipelineConfig.ConfigExists == false)
            {
                EditorGUILayout.HelpBox(
                    $"config.json 을 찾지 못했습니다.\n{DataPipelineConfig.ConfigPath}\n" +
                    "저장하면 기본값으로 새로 만듭니다.", MessageType.Warning);
            }

            EditorGUILayout.HelpBox(
                "상대 경로는 프로젝트 루트(Assets 의 부모) 기준입니다.\n" +
                "위 두 칸은 run_win.bat 이, 아래 네 칸은 DB Generate 가 사용합니다.", MessageType.Info);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("원본 → JSON  (run_win.bat)", EditorStyles.boldLabel);
            _paths.SourceFolder = PathField("원본 폴더", _paths.SourceFolder, true, "원본 폴더 선택");
            _paths.JsonOutput = PathField("JSON 출력", _paths.JsonOutput, true, "JSON 출력 폴더 선택");
            _paths.SchemaOutput = PathField("스키마 파일", _paths.SchemaOutput, false, null);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("스키마 → C#  (DB Generate)", EditorStyles.boldLabel);
            _paths.GeneratedFolder = PathField("생성 코드 폴더", _paths.GeneratedFolder, true, "생성 코드 폴더 선택");
            _paths.ContainersFolder = PathField("컨테이너 폴더", _paths.ContainersFolder, true, "컨테이너 폴더 선택");
            _paths.GameEnumFile = PathField("GameEnum.cs", _paths.GameEnumFile, false, null);
            _paths.GameRootFile = PathField("GameRoot.Generated.cs", _paths.GameRootFile, false, null);

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(6);
            DrawWarnings();

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = _dirty;
                if (GUILayout.Button("저장", GUILayout.Height(24)))
                {
                    if (DataPipelineConfig.Save(_paths))
                    {
                        _dirty = false;
                        AssetDatabase.Refresh();
                        Debug.Log("[GameData] config.json 의 Paths 를 저장했습니다.");
                    }
                }
                GUI.enabled = true;

                if (GUILayout.Button("되돌리기", GUILayout.Height(24), GUILayout.Width(90)))
                {
                    Reload();
                    GUI.FocusControl(null);
                }

                if (GUILayout.Button("기본값", GUILayout.Height(24), GUILayout.Width(90)))
                {
                    _paths = new DataPipelinePaths();
                    _dirty = true;
                    GUI.FocusControl(null);
                }
            }

            if (_dirty)
            {
                EditorGUILayout.LabelField("저장하지 않은 변경이 있습니다.", EditorStyles.miniLabel);
            }
        }

        private string PathField(string label, string value, bool isFolder, string title)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                string next = EditorGUILayout.TextField(label, value);
                if (EditorGUI.EndChangeCheck())
                {
                    _dirty = true;
                    value = next;
                }

                if (isFolder && GUILayout.Button("...", GUILayout.Width(28)))
                {
                    string start = DataPipelineConfig.Resolve(value);
                    if (Directory.Exists(start) == false)
                    {
                        start = DataPipelineConfig.RootPath;
                    }

                    string picked = EditorUtility.OpenFolderPanel(title, start, string.Empty);
                    if (string.IsNullOrEmpty(picked) == false)
                    {
                        value = ToRelative(picked);
                        _dirty = true;
                        GUI.FocusControl(null);
                    }
                }
            }
            return value;
        }

        /// <summary>루트 밑이면 상대 경로로 줄인다. 밖이면 절대 경로 그대로 둔다.</summary>
        private static string ToRelative(string absolute)
        {
            string root = DataPipelineConfig.RootPath;
            string value = absolute.Replace('\\', '/');
            if (value.StartsWith(root + "/"))
            {
                return value.Substring(root.Length + 1);
            }
            return value;
        }

        private void DrawWarnings()
        {
            string source = DataPipelineConfig.Resolve(_paths.SourceFolder);
            if (Directory.Exists(source) == false)
            {
                EditorGUILayout.HelpBox($"원본 폴더가 없습니다: {_paths.SourceFolder}", MessageType.Warning);
            }

            // 스키마는 에디터 전용이다. JSON 폴더가 통째로 Addressables 엔트리가 되므로
            // 그 안에 두면 런타임에 쓰지도 않는 파일이 빌드에 딸려간다.
            string jsonFolder = Normalize(_paths.JsonOutput);
            string schema = Normalize(_paths.SchemaOutput);
            if (string.IsNullOrEmpty(jsonFolder) == false && schema.StartsWith(jsonFolder + "/"))
            {
                EditorGUILayout.HelpBox(
                    "스키마가 JSON 출력 폴더 안에 있습니다. 에디터 전용 파일이 빌드에 포함될 수 있으니 폴더 밖으로 옮기세요.",
                    MessageType.Warning);
            }

            if (DataPipelineConfig.IsInsideAssets(_paths.GeneratedFolder) == false)
            {
                EditorGUILayout.HelpBox(
                    "생성 코드 폴더가 Assets 밖입니다. Unity 가 컴파일하지 않습니다.", MessageType.Error);
            }
            if (DataPipelineConfig.IsInsideAssets(_paths.ContainersFolder) == false)
            {
                EditorGUILayout.HelpBox(
                    "컨테이너 폴더가 Assets 밖입니다. Unity 가 컴파일하지 않습니다.", MessageType.Error);
            }
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace('\\', '/').TrimEnd('/');
        }
    }
}
