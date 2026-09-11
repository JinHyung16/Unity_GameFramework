using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace Game_DataLoader
{
    /// <summary>
    /// JSON 폴더를 Addressables 에 등록한다.
    /// DataManager 는 라벨 하나로 TextAsset 을 긁어오므로, 폴더 엔트리에 그 라벨만 붙으면 된다.
    /// 폴더째 등록하므로 표가 늘어도 다시 할 일이 없다.
    /// </summary>
    public static class AddressablesSetup
    {
        public const string GroupName = "GameData";

        [MenuItem("Tools/GameData/Setup Addressables", false, 30)]
        public static void Setup()
        {
            DataPipelinePaths paths = DataPipelineConfig.Load();
            string folder = paths.JsonOutput;

            if (DataPipelineConfig.IsInsideAssets(folder) == false)
            {
                Debug.LogError($"[GameData] JSON 출력이 Assets 밖이라 Addressables 에 등록할 수 없습니다: {folder}");
                return;
            }

            if (Directory.Exists(DataPipelineConfig.Resolve(folder)) == false)
            {
                Debug.LogError($"[GameData] JSON 폴더가 없습니다: {folder}\nCtrl+G 로 먼저 변환하세요.");
                return;
            }

            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            if (settings == null)
            {
                Debug.LogError("[GameData] Addressables 설정을 만들지 못했습니다. 패키지가 설치돼 있는지 확인하세요.");
                return;
            }

            string guid = AssetDatabase.AssetPathToGUID(folder);
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogError($"[GameData] 폴더를 에셋으로 찾지 못했습니다: {folder}");
                return;
            }

            AddressableAssetGroup group = settings.FindGroup(GroupName);
            if (group == null)
            {
                group = settings.CreateGroup(
                    GroupName, false, false, false, null,
                    typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
            }

            settings.AddLabel(DataManager.GameDataLabel, false);

            AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group, false, false);
            if (entry == null)
            {
                Debug.LogError($"[GameData] 엔트리를 만들지 못했습니다: {folder}");
                return;
            }

            entry.SetLabel(DataManager.GameDataLabel, true, true, false);
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true, false);
            AssetDatabase.SaveAssets();

            Debug.Log(
                $"[GameData] Addressables 등록 완료 — 그룹 '{GroupName}', 폴더 '{folder}', " +
                $"라벨 '{DataManager.GameDataLabel}'");
        }

        /// <summary>JSON 폴더에 라벨이 붙어 있는지. Data Generate 가 끝에 한 번 확인한다.</summary>
        public static bool IsRegistered(string jsonFolder)
        {
            if (DataPipelineConfig.IsInsideAssets(jsonFolder) == false)
            {
                return false;
            }

            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                return false;
            }

            string guid = AssetDatabase.AssetPathToGUID(jsonFolder);
            if (string.IsNullOrEmpty(guid))
            {
                return false;
            }

            AddressableAssetEntry entry = settings.FindAssetEntry(guid);
            return entry != null && entry.labels != null && entry.labels.Contains(DataManager.GameDataLabel);
        }
    }
}
