using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Game_DataLoader
{
    [Serializable]
    public class DataPipelinePaths
    {
        public string SourceFolder = "_DataExporter/GameData";
        public string JsonOutput = "Assets/GameData";
        public string GeneratedFolder = "Assets/Scripts/DataLoader/Generated";
        public string ContainersFolder = "Assets/Scripts/DataLoader/Containers";
        public string GameEnumFile = "Assets/Scripts/DataLoader/GameEnum.cs";
        public string GameRootFile = "Assets/Scripts/Game/Core/GameRoot.Generated.cs";

        public DataPipelinePaths Clone()
        {
            return new DataPipelinePaths
            {
                SourceFolder = SourceFolder,
                JsonOutput = JsonOutput,
                GeneratedFolder = GeneratedFolder,
                ContainersFolder = ContainersFolder,
                GameEnumFile = GameEnumFile,
                GameRootFile = GameRootFile
            };
        }
    }

    /// <summary>
    /// _DataExporter/config.json 의 "Paths" 를 읽고 쓴다.
    /// Data Generate(Ctrl+G) 가 이 경로대로 읽고 쓴다.
    /// 상대 경로는 프로젝트 루트(Assets 의 부모) 기준이다.
    /// </summary>
    public static class DataPipelineConfig
    {
        public const string ConfigRelativePath = "_DataExporter/config.json";
        public const string PathsKey = "Paths";

        public static string RootPath
        {
            get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/'); }
        }

        public static string ConfigPath
        {
            get { return RootPath + "/" + ConfigRelativePath; }
        }

        public static bool ConfigExists
        {
            get { return File.Exists(ConfigPath); }
        }

        /// <summary>루트 기준 상대 경로를 절대 경로로. 이미 절대면 그대로 둔다.</summary>
        public static string Resolve(string relativeOrAbsolute)
        {
            if (string.IsNullOrEmpty(relativeOrAbsolute))
            {
                return null;
            }
            string value = relativeOrAbsolute.Replace('\\', '/');
            if (Path.IsPathRooted(value))
            {
                return value;
            }
            return Path.GetFullPath(Path.Combine(RootPath, value)).Replace('\\', '/');
        }

        /// <summary>AssetDatabase 가 쓰는 "Assets/..." 형태인지</summary>
        public static bool IsInsideAssets(string relativeOrAbsolute)
        {
            if (string.IsNullOrEmpty(relativeOrAbsolute))
            {
                return false;
            }
            string value = relativeOrAbsolute.Replace('\\', '/');
            return value.StartsWith("Assets/") || value == "Assets";
        }

        public static DataPipelinePaths Load()
        {
            var paths = new DataPipelinePaths();
            if (ConfigExists == false)
            {
                return paths;
            }

            try
            {
                JObject root = JObject.Parse(File.ReadAllText(ConfigPath));
                if ((root[PathsKey] is JObject section) == false)
                {
                    return paths;
                }

                paths.SourceFolder = Read(section, "SourceFolder", paths.SourceFolder);
                paths.JsonOutput = Read(section, "JsonOutput", paths.JsonOutput);
                paths.GeneratedFolder = Read(section, "GeneratedFolder", paths.GeneratedFolder);
                paths.ContainersFolder = Read(section, "ContainersFolder", paths.ContainersFolder);
                paths.GameEnumFile = Read(section, "GameEnumFile", paths.GameEnumFile);
                paths.GameRootFile = Read(section, "GameRootFile", paths.GameRootFile);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DataPipelineConfig] config.json 읽기 실패, 기본값을 씁니다: {e.Message}");
            }
            return paths;
        }

        /// <summary>Paths 섹션만 갈아끼운다. 익스포터가 쓰는 다른 키는 건드리지 않는다.</summary>
        public static bool Save(DataPipelinePaths paths)
        {
            if (paths == null)
            {
                return false;
            }

            JObject root;
            try
            {
                root = ConfigExists ? JObject.Parse(File.ReadAllText(ConfigPath)) : new JObject();
            }
            catch (Exception e)
            {
                Debug.LogError($"[DataPipelineConfig] config.json 파싱 실패로 저장을 멈춥니다: {e.Message}");
                return false;
            }

            var section = new JObject
            {
                ["SourceFolder"] = Normalize(paths.SourceFolder),
                ["JsonOutput"] = Normalize(paths.JsonOutput),
                ["GeneratedFolder"] = Normalize(paths.GeneratedFolder),
                ["ContainersFolder"] = Normalize(paths.ContainersFolder),
                ["GameEnumFile"] = Normalize(paths.GameEnumFile),
                ["GameRootFile"] = Normalize(paths.GameRootFile)
            };

            // Paths 를 항상 맨 앞에 두어 사람이 열었을 때 먼저 보이게 한다.
            root.Remove(PathsKey);
            root.AddFirst(new JProperty(PathsKey, section));

            try
            {
                string dir = Path.GetDirectoryName(ConfigPath);
                if (Directory.Exists(dir) == false)
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(ConfigPath, root.ToString(Newtonsoft.Json.Formatting.Indented));
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[DataPipelineConfig] config.json 저장 실패: {e.Message}");
                return false;
            }
        }

        private static string Read(JObject section, string key, string fallback)
        {
            JToken token = section[key];
            if (token == null || token.Type == JTokenType.Null)
            {
                return fallback;
            }
            string value = token.ToString().Trim();
            return value.Length == 0 ? fallback : Normalize(value);
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrEmpty(value) ? value : value.Replace('\\', '/').TrimEnd('/');
        }
    }
}
