using System.Collections.Generic;
using System.IO;

namespace Game_DataLoader
{
    /// <summary>
    /// .ts 표를 읽는다. node 로는 바로 실행되지 않으므로 tsx 를 거친다.
    /// tsx 가 없으면 이 확장자만 비활성으로 두고 나머지는 그대로 변환된다.
    ///   설치: npm i -g tsx   (또는 프로젝트에 devDependency 로)
    /// </summary>
    public sealed class TsSourceLoader : IDataSourceLoader
    {
        public string Extension => ".ts";
        public string DisplayName => "TypeScript (tsx)";
        public int Order => 25;

        private const string RunnerTemplate =
            "import {{ pathToFileURL }} from 'url';" +
            "const loaded = await import(pathToFileURL({0}).href);" +
            "const tables = loaded.default ?? loaded.TABLES ?? loaded;" +
            "process.stdout.write(JSON.stringify(tables));";

        private static string FindTsx()
        {
            return ExternalRuntime.Find("tsx", "TSX", "tsx", "npx");
        }

        public bool IsAvailable(out string reason)
        {
            if (FindTsx() != null)
            {
                reason = null;
                return true;
            }
            reason = "tsx 를 찾지 못했습니다. 'npm i -g tsx' 로 설치하거나 TSX 환경변수로 경로를 지정하세요.";
            return false;
        }

        public IEnumerable<SourceTable> Load(string filePath, DataIssueLog log)
        {
            string fileName = Path.GetFileName(filePath);

            string tsx = FindTsx();
            if (tsx == null)
            {
                log.Error(IssueStage.Load, "tsx 를 찾지 못해 건너뜁니다.", fileName);
                return new List<SourceTable>();
            }

            string full = Path.GetFullPath(filePath).Replace("\\", "/");
            string script = string.Format(RunnerTemplate, "'" + full + "'");

            var args = new List<string>();
            if (Path.GetFileNameWithoutExtension(tsx).ToLowerInvariant() == "npx")
            {
                args.Add("--yes");
                args.Add("tsx");
            }
            args.Add("--eval");
            args.Add(script);

            int exit = ExternalRuntime.Run(
                tsx, args.ToArray(), Path.GetDirectoryName(Path.GetFullPath(filePath)),
                out string stdout, out string stderr);

            if (exit != 0)
            {
                log.Error(IssueStage.Load, $"tsx 실행 실패: {JsSourceLoader.Summarize(stderr)}", fileName);
                return new List<SourceTable>();
            }

            return ScriptTableReader.FromJson(stdout, fileName, log);
        }
    }
}
