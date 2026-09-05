using System;
using System.Collections.Generic;

namespace Game_DataLoader
{
    /// <summary>
    /// _Enum 표에서 enum 정의를 모은다.
    /// 규약이 일반 표와 다르다 — 열 하나가 enum 하나이고, 머리글이 이름, 그 아래가 멤버다.
    /// 값(정수)은 등장 순서대로 0,1,2... 가 배정되므로 순서를 보존한다.
    /// </summary>
    public sealed class EnumCollector
    {
        private readonly List<string> _order = new List<string>();
        private readonly Dictionary<string, List<string>> _members = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _seen = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        public IReadOnlyList<string> Order => _order;
        public int Count => _order.Count;

        public IReadOnlyList<string> MembersOf(string enumName)
        {
            return _members.TryGetValue(enumName, out List<string> list) ? list : null;
        }

        public HashSet<string> NameSet()
        {
            return new HashSet<string>(_order, StringComparer.Ordinal);
        }

        /// <summary>
        /// enum 이름 규칙: 앞의 E 를 떼고 'Type' 으로 끝나게 한다.
        /// (EStatType -> StatType, ECurrency -> CurrencyType)
        /// </summary>
        public static string NormalizeName(string raw)
        {
            string n = (raw ?? string.Empty).Trim();
            if (n.Length >= 2 && n[0] == 'E' && char.IsUpper(n[1]))
            {
                n = n.Substring(1);
            }
            if (n.EndsWith("Type") == false)
            {
                n += "Type";
            }
            return n;
        }

        public void Collect(SourceTable table, DataIssueLog log)
        {
            if (table.Rows.Count < 2)
            {
                log.Warning(IssueStage.Load,
                    "enum 표에 머리글과 멤버가 없습니다. 열 하나가 enum 하나이고 첫 행이 이름입니다.",
                    table.SourceFile, table.Name);
                return;
            }

            IReadOnlyList<object> header = table.Rows[0];

            for (int c = 0; c < header.Count; c++)
            {
                string rawName = SourceTableConverter.CellToText(c < header.Count ? header[c] : null);
                if (rawName.Length == 0 || rawName.StartsWith("#"))
                {
                    continue;
                }

                string name = NormalizeName(rawName);
                if (name != rawName)
                {
                    log.Warning(IssueStage.Load,
                        $"enum 이름 '{rawName}' 을 '{name}' 으로 맞췄습니다. 원본도 바꾸는 편이 좋습니다.",
                        table.SourceFile, table.Name, SourceTable.ToDisplayRow(0), rawName);
                }

                if (_members.ContainsKey(name) == false)
                {
                    _members[name] = new List<string>();
                    _seen[name] = new HashSet<string>(StringComparer.Ordinal);
                    _order.Add(name);
                }

                List<string> members = _members[name];
                HashSet<string> seen = _seen[name];

                for (int r = 1; r < table.Rows.Count; r++)
                {
                    IReadOnlyList<object> row = table.Rows[r];
                    string rawMember = SourceTableConverter.CellToText(c < row.Count ? row[c] : null);
                    if (rawMember.Length == 0 || rawMember.StartsWith("#"))
                    {
                        continue;
                    }

                    string member = Sanitize(rawMember);
                    if (member.Length == 0)
                    {
                        continue;
                    }

                    if (member != rawMember)
                    {
                        log.Warning(IssueStage.Load,
                            $"enum 멤버 '{rawMember}' 은 C# 식별자로 쓸 수 없어 '{member}' 로 바꿉니다.",
                            table.SourceFile, table.Name, SourceTable.ToDisplayRow(r), name);
                    }

                    if (seen.Add(member) == false)
                    {
                        log.Warning(IssueStage.Load,
                            $"enum '{name}' 에 '{member}' 가 이미 있습니다. 뒤쪽을 건너뜁니다.",
                            table.SourceFile, table.Name, SourceTable.ToDisplayRow(r), name);
                        continue;
                    }

                    members.Add(member);
                }
            }
        }

        public void ReportEmpty(DataIssueLog log)
        {
            foreach (string name in _order)
            {
                if (_members[name].Count == 0)
                {
                    log.Warning(IssueStage.Load, $"enum '{name}' 에 멤버가 없습니다.");
                }
            }
        }

        /// <summary>C# 식별자로 쓸 수 없는 문자를 '_' 로 바꾼다. 숫자로 시작하면 앞에 '_' 를 붙인다.</summary>
        public static string Sanitize(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            var sb = new System.Text.StringBuilder(raw.Length);
            foreach (char c in raw.Trim())
            {
                bool valid = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';
                sb.Append(valid ? c : '_');
            }
            if (sb.Length > 0 && sb[0] >= '0' && sb[0] <= '9')
            {
                sb.Insert(0, '_');
            }
            return sb.ToString();
        }
    }
}
