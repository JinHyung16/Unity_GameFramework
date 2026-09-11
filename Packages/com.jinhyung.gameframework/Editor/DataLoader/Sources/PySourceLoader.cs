using System.Collections.Generic;
using System.IO;

namespace Game_DataLoader
{
    /// <summary>
    /// .py 표를 읽는다. 대상 파일을 모듈로 로드해 TABLES 딕셔너리를 JSON 으로 받아온다.
    /// </summary>
    public sealed class PySourceLoader : IDataSourceLoader
    {
        public string Extension => ".py";
        public string DisplayName => "Python";
        public int Order => 30;

        private const string Runner =
            "import importlib.util, json, pathlib, sys\n" +
            "p = pathlib.Path(sys.argv[1])\n" +
            "spec = importlib.util.spec_from_file_location(p.stem, str(p))\n" +
            "m = importlib.util.module_from_spec(spec)\n" +
            "spec.loader.exec_module(m)\n" +
            "data = getattr(m, 'TABLES', None)\n" +
            "if data is None: data = getattr(m, 'tables', None)\n" +
            "if data is None: raise SystemExit('TABLES 딕셔너리를 찾을 수 없습니다')\n" +
            "sys.stdout.write(json.dumps(data, ensure_ascii=False, default=str))\n";

        public static string FindPython()
        {
            return ExternalRuntime.Find("python", "PYTHON", "python", "python3", "py");
        }

        public bool IsAvailable(out string reason)
        {
            if (FindPython() != null)
            {
                reason = null;
                return true;
            }
            reason = "python 을 찾지 못했습니다. Python 3 을 설치하거나 PYTHON 환경변수로 경로를 지정하세요.";
            return false;
        }

        public IEnumerable<SourceTable> Load(string filePath, DataIssueLog log)
        {
            string fileName = Path.GetFileName(filePath);

            string python = FindPython();
            if (python == null)
            {
                log.Error(IssueStage.Load, "python 을 찾지 못해 건너뜁니다.", fileName);
                return new List<SourceTable>();
            }

            int exit = ExternalRuntime.Run(
                python,
                new[] { "-c", Runner, Path.GetFullPath(filePath) },
                Path.GetDirectoryName(Path.GetFullPath(filePath)),
                out string stdout, out string stderr);

            if (exit != 0)
            {
                log.Error(IssueStage.Load, $"python 실행 실패: {JsSourceLoader.Summarize(stderr)}", fileName);
                return new List<SourceTable>();
            }

            return ScriptTableReader.FromJson(stdout, fileName, log);
        }
    }
}
