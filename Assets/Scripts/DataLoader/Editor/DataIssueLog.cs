using System.Collections.Generic;
using System.Text;

namespace Game_DataLoader
{
    public enum IssueLevel
    {
        Warning,
        Error
    }

    public enum IssueStage
    {
        Load,
        Generate
    }

    /// <summary>
    /// 어디서 무엇이 잘못됐는지 한 건. 사람이 원본을 열어 고칠 수 있을 만큼의 위치를 담는다.
    /// Row 는 원본에서 보이는 행 번호(1부터)다. 모르면 0.
    /// </summary>
    public sealed class DataIssue
    {
        public IssueLevel Level;
        public IssueStage Stage;
        public string File;
        public string Table;
        public int Row;
        public string Column;
        public string Message;

        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append(Level == IssueLevel.Error ? "[에러] " : "[경고] ");

            if (string.IsNullOrEmpty(File) == false)
            {
                sb.Append(File);
            }
            if (string.IsNullOrEmpty(Table) == false)
            {
                sb.Append(" > ").Append(Table);
            }

            bool hasRow = Row > 0;
            bool hasColumn = string.IsNullOrEmpty(Column) == false;
            if (hasRow || hasColumn)
            {
                sb.Append(" [");
                if (hasRow)
                {
                    sb.Append(Row).Append("행");
                }
                if (hasRow && hasColumn)
                {
                    sb.Append(' ');
                }
                if (hasColumn)
                {
                    sb.Append(Column);
                }
                sb.Append(']');
            }

            sb.Append(" — ").Append(Message);
            return sb.ToString();
        }
    }

    /// <summary>
    /// 변환·생성 한 번 동안 모인 문제들.
    /// 로더와 생성기가 같은 통로로 보고하므로, 어느 단계에서 깨졌는지 한자리에서 볼 수 있다.
    /// </summary>
    public sealed class DataIssueLog
    {
        private readonly List<DataIssue> _issues = new List<DataIssue>();

        public IReadOnlyList<DataIssue> Issues => _issues;
        public int ErrorCount { get; private set; }
        public int WarningCount { get; private set; }
        public bool HasError => ErrorCount > 0;
        public bool HasAny => _issues.Count > 0;

        public void Error(IssueStage stage, string message, string file = null, string table = null, int row = 0, string column = null)
        {
            Add(IssueLevel.Error, stage, message, file, table, row, column);
        }

        public void Warning(IssueStage stage, string message, string file = null, string table = null, int row = 0, string column = null)
        {
            Add(IssueLevel.Warning, stage, message, file, table, row, column);
        }

        private void Add(IssueLevel level, IssueStage stage, string message, string file, string table, int row, string column)
        {
            _issues.Add(new DataIssue
            {
                Level = level,
                Stage = stage,
                File = file,
                Table = table,
                Row = row,
                Column = column,
                Message = message
            });

            if (level == IssueLevel.Error)
            {
                ErrorCount++;
            }
            else
            {
                WarningCount++;
            }
        }

        public string Summary()
        {
            if (HasAny == false)
            {
                return "문제 없음";
            }
            return $"에러 {ErrorCount}건, 경고 {WarningCount}건";
        }

        /// <summary>에러를 먼저, 그 다음 경고를 나열한다.</summary>
        public string BuildReport(int maxLines = 200)
        {
            if (HasAny == false)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            int written = 0;

            for (int pass = 0; pass < 2; pass++)
            {
                IssueLevel want = pass == 0 ? IssueLevel.Error : IssueLevel.Warning;
                foreach (DataIssue issue in _issues)
                {
                    if (issue.Level != want)
                    {
                        continue;
                    }
                    if (written >= maxLines)
                    {
                        sb.AppendLine($"... 외 {_issues.Count - written}건");
                        return sb.ToString();
                    }
                    sb.AppendLine(issue.Describe());
                    written++;
                }
            }

            return sb.ToString();
        }
    }
}
