using System.Collections.Generic;
using System.IO;

namespace Game_DataLoader
{
    /// <summary>
    /// .js 표를 읽는다. node 를 불러 module.exports 를 JSON 으로 받아온다.
    /// 계산으로 표를 만드는 용도(레벨 커브 등)라 실제로 코드를 실행해야 값이 나온다.
    /// </summary>
    public sealed class JsSourceLoader : IDataSourceLoader
    {
        public string Extension => ".js";
        public string DisplayName => "JavaScript (node)";
        public int Order => 20;

        // 대상 파일을 require 해서 표 묶음을 stdout 으로 흘린다.
        // export default 로 쓴 경우도 받아준다.
        private const string Runner =
            "const path=require('path');" +
            "const loaded=require(path.resolve(process.argv[1]));" +
            "const tables=(loaded&&loaded.default)?loaded.default:loaded;" +
            "process.stdout.write(JSON.stringify(tables));";

        public static string FindNode()
        {
            return ExternalRuntime.Find("node", "NODE", "node");
        }

        public bool IsAvailable(out string reason)
        {
            if (FindNode() != null)
            {
                reason = null;
                return true;
            }
            reason = "node 를 찾지 못했습니다. Node.js 를 설치하거나 NODE 환경변수로 경로를 지정하세요.";
            return false;
        }

        public IEnumerable<SourceTable> Load(string filePath, DataIssueLog log)
        {
            string fileName = Path.GetFileName(filePath);

            string node = FindNode();
            if (node == null)
            {
                log.Error(IssueStage.Load, "node 를 찾지 못해 건너뜁니다.", fileName);
                return new List<SourceTable>();
            }

            int exit = ExternalRuntime.Run(
                node,
                new[] { "-e", Runner, Path.GetFullPath(filePath) },
                Path.GetDirectoryName(Path.GetFullPath(filePath)),
                out string stdout, out string stderr);

            if (exit != 0)
            {
                log.Error(IssueStage.Load, $"node 실행 실패: {Summarize(stderr)}", fileName);
                return new List<SourceTable>();
            }

            return ScriptTableReader.FromJson(stdout, fileName, log);
        }

        internal static string Summarize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "(출력 없음)";
            }
            string[] lines = text.Split('\n');
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0)
                {
                    return trimmed.Length > 300 ? trimmed.Substring(0, 300) + "..." : trimmed;
                }
            }
            return "(출력 없음)";
        }
    }
}
