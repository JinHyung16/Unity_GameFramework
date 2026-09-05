using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace Game_DataLoader
{
    public sealed class ExportResult
    {
        public int TableCount;
        public int RowCount;
        public int EnumCount;
        public int FileCount;
        public List<string> WrittenJson = new List<string>();

        /// <summary>코드 생성에 그대로 넘긴다. 같은 실행 안이라 파일을 거칠 이유가 없다.</summary>
        public List<ConvertedTable> Tables = new List<ConvertedTable>();
        public EnumCollector Enums;
    }

    /// <summary>
    /// 원본 폴더 → JSON.
    /// 확장자별 로더가 읽은 것을 한 규칙으로 변환해 내보내고,
    /// 자료형까지 풀어둔 결과를 ExportResult 에 담아 코드 생성 쪽에 넘긴다.
    /// </summary>
    public static class DataExportPipeline
    {
        /// <summary>파일 이름에 이 말이 들어가면 enum 정의로 본다.</summary>
        public const string EnumFileMarker = "_Enum";

        public static ExportResult Run(DataPipelinePaths paths, DataIssueLog log)
        {
            var result = new ExportResult();

            string sourceFolder = DataPipelineConfig.Resolve(paths.SourceFolder);
            if (Directory.Exists(sourceFolder) == false)
            {
                log.Error(IssueStage.Load, $"원본 폴더가 없습니다: {paths.SourceFolder}");
                return result;
            }

            var enums = new EnumCollector();
            var dataTables = new List<SourceTable>();

            // enum 을 먼저 모은다. 표의 자료형 칸이 enum 이름을 쓰므로 순서가 중요하다.
            foreach (string file in EnumerateSourceFiles(sourceFolder, log))
            {
                IDataSourceLoader loader = DataSourceRegistry.Find(Path.GetExtension(file));
                if (loader == null)
                {
                    continue;
                }

                result.FileCount++;
                bool isEnumFile = Path.GetFileNameWithoutExtension(file)
                    .IndexOf(EnumFileMarker, StringComparison.OrdinalIgnoreCase) >= 0;

                foreach (SourceTable table in loader.Load(file, log))
                {
                    if (isEnumFile)
                    {
                        enums.Collect(table, log);
                    }
                    else
                    {
                        dataTables.Add(table);
                    }
                }
            }

            enums.ReportEmpty(log);
            result.EnumCount = enums.Count;

            HashSet<string> knownEnums = enums.NameSet();
            string jsonFolder = DataPipelineConfig.Resolve(paths.JsonOutput);
            Directory.CreateDirectory(jsonFolder);

            result.Enums = enums;
            var usedNames = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (SourceTable table in dataTables)
            {
                if (usedNames.TryGetValue(table.Name, out string firstFile))
                {
                    log.Error(IssueStage.Load,
                        $"표 이름 '{table.Name}' 이 {firstFile} 과 겹칩니다. 뒤쪽을 건너뜁니다.",
                        table.SourceFile, table.Name);
                    continue;
                }
                usedNames[table.Name] = table.SourceFile;

                ConvertedTable converted = SourceTableConverter.Convert(table, knownEnums, log);
                if (converted.Columns.Count == 0)
                {
                    continue;
                }

                string jsonPath = Path.Combine(jsonFolder, converted.Name + ".json");
                if (WriteIfChanged(jsonPath, SourceTableConverter.ToJson(converted.Rows), log, table.SourceFile))
                {
                    result.WrittenJson.Add(converted.Name + ".json");
                }

                result.Tables.Add(converted);
                result.TableCount++;
                result.RowCount += converted.Rows.Count;
            }

            // 표를 하나도 못 읽었으면 생성 코드를 건드리지 않도록 알린다.
            // 원본 폴더를 잘못 잡았거나 옮겼을 때 작업물이 날아가는 것을 막는 자리다.
            if (result.TableCount == 0)
            {
                if (result.FileCount == 0)
                {
                    log.Error(IssueStage.Load,
                        $"원본을 하나도 찾지 못했습니다: {paths.SourceFolder}" +
                        " — 경로가 맞는지 Tools > GameData > Settings 에서 확인하세요. 생성 코드는 그대로 둡니다.");
                }
                else
                {
                    log.Error(IssueStage.Load,
                        $"원본 {result.FileCount}개를 읽었지만 쓸 수 있는 표가 없습니다. 생성 코드는 그대로 둡니다.");
                }
            }

            return result;
        }

        private static IEnumerable<string> EnumerateSourceFiles(string folder, DataIssueLog log)
        {
            var files = new List<string>();
            foreach (string path in Directory.GetFiles(folder))
            {
                string name = Path.GetFileName(path);

                // 엑셀 임시 파일(~$...) 과 숨김 파일은 건너뛴다.
                if (name.StartsWith("~") || name.StartsWith("."))
                {
                    continue;
                }

                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (string.IsNullOrEmpty(ext))
                {
                    continue;
                }

                IDataSourceLoader loader = DataSourceRegistry.Find(ext);
                if (loader == null)
                {
                    continue;
                }

                if (loader.IsAvailable(out string reason) == false)
                {
                    log.Warning(IssueStage.Load, $"{loader.DisplayName} 로더를 쓸 수 없어 건너뜁니다. {reason}", name);
                    continue;
                }

                files.Add(path);
            }

            // enum 정의를 먼저 읽어야 표의 자료형에서 enum 이름을 알아본다.
            files.Sort((a, b) =>
            {
                bool ea = Path.GetFileNameWithoutExtension(a).IndexOf(EnumFileMarker, StringComparison.OrdinalIgnoreCase) >= 0;
                bool eb = Path.GetFileNameWithoutExtension(b).IndexOf(EnumFileMarker, StringComparison.OrdinalIgnoreCase) >= 0;
                if (ea != eb)
                {
                    return ea ? -1 : 1;
                }
                return string.Compare(a, b, StringComparison.Ordinal);
            });

            return files;
        }

        private static bool WriteIfChanged(string path, string content, DataIssueLog log, string sourceFile)
        {
            try
            {
                if (File.Exists(path) && File.ReadAllText(path) == content)
                {
                    return false;
                }
                File.WriteAllText(path, content);
                return true;
            }
            catch (Exception e)
            {
                log.Error(IssueStage.Load, $"파일을 쓰지 못했습니다 ({Path.GetFileName(path)}): {e.Message}", sourceFile);
                return false;
            }
        }
    }
}
