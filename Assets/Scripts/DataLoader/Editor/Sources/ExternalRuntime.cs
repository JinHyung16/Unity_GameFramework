using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Game_DataLoader
{
    /// <summary>
    /// node·python 처럼 밖에 있는 실행기를 찾아 돌린다.
    /// 스크립트 포맷 로더(js·ts·py)가 공통으로 쓴다.
    /// </summary>
    public static class ExternalRuntime
    {
        private static readonly Dictionary<string, string> _resolved = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>컴파일 후·설정 변경 후 다시 찾게 한다.</summary>
        public static void Invalidate()
        {
            _resolved.Clear();
        }

        /// <summary>
        /// 후보 명령을 순서대로 --version 으로 찔러보고 되는 것을 캐시한다.
        /// envVar 가 지정돼 있으면 그 값을 최우선으로 쓴다.
        /// </summary>
        public static string Find(string cacheKey, string envVar, params string[] candidates)
        {
            if (_resolved.TryGetValue(cacheKey, out string cached))
            {
                return cached;
            }

            var order = new List<string>();
            if (string.IsNullOrEmpty(envVar) == false)
            {
                string fromEnv = Environment.GetEnvironmentVariable(envVar);
                if (string.IsNullOrEmpty(fromEnv) == false)
                {
                    order.Add(fromEnv);
                }
            }
            order.AddRange(candidates);

            foreach (string candidate in order)
            {
                if (CanRun(candidate))
                {
                    _resolved[cacheKey] = candidate;
                    return candidate;
                }
            }

            _resolved[cacheKey] = null;
            return null;
        }

        private static bool CanRun(string command)
        {
            try
            {
                return Run(command, new[] { "--version" }, null, out _, out _, 5000) == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 프로세스를 돌리고 표준 출력·오류를 받는다. 반환값은 종료 코드, 실패하면 -1.
        /// stdout 을 스트림으로 읽어 큰 표에서도 버퍼가 막히지 않게 한다.
        /// </summary>
        public static int Run(string command, string[] args, string workingDirectory,
            out string stdout, out string stderr, int timeoutMs = 120000)
        {
            stdout = string.Empty;
            stderr = string.Empty;

            var info = new ProcessStartInfo
            {
                FileName = command,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            foreach (string arg in args)
            {
                info.ArgumentList.Add(arg);
            }

            if (string.IsNullOrEmpty(workingDirectory) == false)
            {
                info.WorkingDirectory = workingDirectory;
            }

            // 파이썬이 한글을 콘솔 코드페이지로 내보내 깨지는 것을 막는다.
            info.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

            var outBuilder = new StringBuilder();
            var errBuilder = new StringBuilder();

            try
            {
                using (var process = new Process { StartInfo = info })
                {
                    process.OutputDataReceived += (_, e) =>
                    {
                        if (e.Data != null)
                        {
                            outBuilder.Append(e.Data).Append('\n');
                        }
                    };
                    process.ErrorDataReceived += (_, e) =>
                    {
                        if (e.Data != null)
                        {
                            errBuilder.Append(e.Data).Append('\n');
                        }
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    if (process.WaitForExit(timeoutMs) == false)
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch (Exception)
                        {
                            // 이미 끝났으면 무시
                        }
                        stderr = $"시간 초과 ({timeoutMs}ms)";
                        return -1;
                    }

                    // 비동기 읽기가 남은 것을 마저 비운다.
                    process.WaitForExit();

                    stdout = outBuilder.ToString();
                    stderr = errBuilder.ToString();
                    return process.ExitCode;
                }
            }
            catch (Exception e)
            {
                stderr = e.Message;
                return -1;
            }
        }
    }
}
