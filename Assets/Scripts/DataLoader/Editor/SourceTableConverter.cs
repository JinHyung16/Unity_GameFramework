using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Game_DataLoader
{
    /// <summary>
    /// 열 하나의 규격. 자료형 칸의 표기를 풀어놓은 것이다.
    ///   int!    필수  — 빈 값이면 에러
    ///   #int    제외  — JSON 에도 C# 에도 나가지 않는다
    /// </summary>
    public sealed class ColumnSpec
    {
        public int Index;
        public string Name;
        public string Type;
        public string RawType;
        public bool Required;
        public bool Excluded;
        public string ExcludeReason;
    }

    public sealed class ConvertedTable
    {
        public string Name;
        public string SourceFile;
        public JArray Rows = new JArray();
        public List<ColumnSpec> Columns = new List<ColumnSpec>();
    }

    /// <summary>
    /// SourceTable(원본 행 배열) → JSON 행 + 열 규격.
    /// 확장자와 무관하게 여기 한 곳에서만 자료형을 해석하므로 포맷이 늘어도 규칙이 갈리지 않는다.
    /// </summary>
    public static class SourceTableConverter
    {
        private const int MaxConsecutiveEmptyRows = 3;

        public static ConvertedTable Convert(SourceTable table, HashSet<string> knownEnums, DataIssueLog log)
        {
            var result = new ConvertedTable { Name = table.Name, SourceFile = table.SourceFile };

            if (table.Rows.Count < 3)
            {
                log.Warning(IssueStage.Load,
                    "행이 3개 미만입니다. 1행 열 이름, 2행 자료형, 3행부터 데이터여야 합니다.",
                    table.SourceFile, table.Name);
                return result;
            }

            IReadOnlyList<object> headerRow = table.Rows[0];
            IReadOnlyList<object> typeRow = table.Rows[1];

            List<ColumnSpec> columns = ParseColumns(headerRow, typeRow, table, knownEnums, log);
            foreach (ColumnSpec column in columns)
            {
                if (column.Excluded == false)
                {
                    result.Columns.Add(column);
                }
            }

            if (result.Columns.Count == 0)
            {
                log.Warning(IssueStage.Load, "쓸 수 있는 열이 없습니다.", table.SourceFile, table.Name);
                return result;
            }

            int emptyStreak = 0;
            for (int r = 2; r < table.Rows.Count; r++)
            {
                IReadOnlyList<object> row = table.Rows[r];
                int displayRow = SourceTable.ToDisplayRow(r);

                if (IsRowEmpty(row, result.Columns))
                {
                    emptyStreak++;
                    if (emptyStreak >= MaxConsecutiveEmptyRows)
                    {
                        // 표 아래쪽 여백이다. 여기서 끊지 않으면 빈 행을 계속 훑는다.
                        break;
                    }
                    continue;
                }
                emptyStreak = 0;

                var item = new JObject();
                foreach (ColumnSpec column in result.Columns)
                {
                    object cell = column.Index < row.Count ? row[column.Index] : null;
                    item[column.Name] = ConvertCell(cell, column, knownEnums, log, table, displayRow);
                }
                result.Rows.Add(item);
            }

            return result;
        }

        private static List<ColumnSpec> ParseColumns(
            IReadOnlyList<object> headerRow, IReadOnlyList<object> typeRow, SourceTable table,
            HashSet<string> knownEnums, DataIssueLog log)
        {
            var columns = new List<ColumnSpec>();
            var usedNames = new HashSet<string>(StringComparer.Ordinal);

            int width = Math.Max(headerRow.Count, typeRow.Count);
            for (int i = 0; i < width; i++)
            {
                string name = CellToText(i < headerRow.Count ? headerRow[i] : null);
                string rawType = CellToText(i < typeRow.Count ? typeRow[i] : null);

                var spec = new ColumnSpec { Index = i, Name = name, RawType = rawType };

                if (rawType.Length == 0)
                {
                    spec.Excluded = true;
                    spec.ExcludeReason = "자료형 없음";
                    columns.Add(spec);
                    continue;
                }

                // 자료형 앞의 '#' 은 "이 열은 내보내지 않는다"는 표시다.
                if (rawType.StartsWith("#"))
                {
                    spec.Excluded = true;
                    spec.ExcludeReason = "# 로 제외";
                    columns.Add(spec);
                    continue;
                }

                if (name.Length == 0)
                {
                    spec.Excluded = true;
                    spec.ExcludeReason = "열 이름 없음";
                    log.Warning(IssueStage.Load,
                        $"자료형('{rawType}')은 있는데 열 이름이 비어 있어 건너뜁니다.",
                        table.SourceFile, table.Name, SourceTable.ToDisplayRow(0), "열 " + (i + 1));
                    columns.Add(spec);
                    continue;
                }

                if (name.StartsWith("_") || name.Contains("~"))
                {
                    spec.Excluded = true;
                    spec.ExcludeReason = "열 이름 규칙(_ 또는 ~)";
                    columns.Add(spec);
                    continue;
                }

                string type = rawType;
                if (type.EndsWith("!"))
                {
                    spec.Required = true;
                    type = type.Substring(0, type.Length - 1).Trim();
                }

                if (type.Length == 0)
                {
                    spec.Excluded = true;
                    spec.ExcludeReason = "자료형 없음";
                    log.Warning(IssueStage.Load, $"'{name}' 열의 자료형이 '!' 뿐입니다.",
                        table.SourceFile, table.Name, SourceTable.ToDisplayRow(1), name);
                    columns.Add(spec);
                    continue;
                }

                spec.Type = type;

                if (usedNames.Add(name) == false)
                {
                    spec.Excluded = true;
                    spec.ExcludeReason = "열 이름 중복";
                    log.Error(IssueStage.Load,
                        $"열 이름 '{name}' 이 중복됩니다. 뒤쪽 열을 건너뜁니다.",
                        table.SourceFile, table.Name, SourceTable.ToDisplayRow(0), name);
                }

                // 자료형 검증은 여기서 한 번만 한다. 행마다 검사하면 같은 메시지가 행 수만큼 쌓인다.
                ValidateType(spec, table, knownEnums, log);

                columns.Add(spec);
            }

            return columns;
        }

        /// <summary>열 하나의 자료형이 쓸 수 있는 표기인지. 문제가 있으면 열 단위로 한 번만 보고한다.</summary>
        private static void ValidateType(ColumnSpec spec, SourceTable table, HashSet<string> knownEnums, DataIssueLog log)
        {
            if (knownEnums != null && knownEnums.Contains(spec.Type))
            {
                return;
            }

            int typeRow = SourceTable.ToDisplayRow(1);
            switch (spec.Type.ToLowerInvariant())
            {
                case "int":
                case "integer":
                case "long":
                case "float":
                case "double":
                case "number":
                case "bool":
                case "boolean":
                case "intarray":
                case "longarray":
                case "floatarray":
                case "stringarray":
                case "string":
                case "text":
                    return;

                case "array":
                    log.Error(IssueStage.Load,
                        "'array' 는 쓸 수 없습니다. intArray / floatArray / stringArray 중 하나를 쓰세요.",
                        table.SourceFile, table.Name, typeRow, spec.Name);
                    return;

                case "json":
                    log.Error(IssueStage.Load,
                        "'json' 은 쓸 수 없습니다. 기본 자료형이나 enum 으로 정규화하세요.",
                        table.SourceFile, table.Name, typeRow, spec.Name);
                    return;

                default:
                    // 대문자로 시작하면 enum 을 의도한 것으로 본다. 정의가 없으면 알려준다.
                    if (spec.Type.Length > 0 && char.IsUpper(spec.Type[0]))
                    {
                        log.Error(IssueStage.Load,
                            $"알 수 없는 자료형 '{spec.Type}' 입니다. enum 이라면 _Enum 정의에 추가하세요. 우선 string 으로 처리합니다.",
                            table.SourceFile, table.Name, typeRow, spec.Name);
                    }
                    else
                    {
                        log.Warning(IssueStage.Load,
                            $"모르는 자료형 '{spec.Type}' 입니다. string 으로 처리합니다.",
                            table.SourceFile, table.Name, typeRow, spec.Name);
                    }
                    return;
            }
        }

        private static bool IsRowEmpty(IReadOnlyList<object> row, List<ColumnSpec> columns)
        {
            foreach (ColumnSpec column in columns)
            {
                if (column.Index >= row.Count)
                {
                    continue;
                }
                if (CellToText(row[column.Index]).Length > 0)
                {
                    return false;
                }
            }
            return true;
        }

        private static JToken ConvertCell(
            object cell, ColumnSpec column, HashSet<string> knownEnums, DataIssueLog log, SourceTable table, int displayRow)
        {
            string text = CellToText(cell);

            if (text.Length == 0)
            {
                if (column.Required)
                {
                    log.Error(IssueStage.Load,
                        $"필수 열이 비어 있습니다. 자료형 '{column.RawType}'",
                        table.SourceFile, table.Name, displayRow, column.Name);
                    return DefaultOf(column.Type);
                }
                return JValue.CreateNull();
            }

            if (knownEnums != null && knownEnums.Contains(column.Type))
            {
                return new JValue(text);
            }

            switch (column.Type.ToLowerInvariant())
            {
                case "int":
                case "integer":
                case "long":
                {
                    string clean = text.Replace(",", string.Empty);
                    if (long.TryParse(clean, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
                    {
                        return new JValue(value);
                    }
                    // 엑셀이 정수를 실수로 저장하는 경우가 있다 (10 -> 10.0)
                    if (double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out double asDouble)
                        && Math.Abs(asDouble % 1) < double.Epsilon)
                    {
                        return new JValue((long)asDouble);
                    }
                    log.Warning(IssueStage.Load, $"정수로 바꿀 수 없습니다: '{text}'",
                        table.SourceFile, table.Name, displayRow, column.Name);
                    return column.Required ? new JValue(0L) : JValue.CreateNull();
                }

                case "float":
                case "double":
                case "number":
                {
                    string clean = text.Replace(",", string.Empty);
                    if (double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    {
                        return FromDouble(value);
                    }
                    log.Warning(IssueStage.Load, $"숫자로 바꿀 수 없습니다: '{text}'",
                        table.SourceFile, table.Name, displayRow, column.Name);
                    return column.Required ? FromDouble(0d) : JValue.CreateNull();
                }

                case "bool":
                case "boolean":
                {
                    string lower = text.ToLowerInvariant();
                    if (lower == "true" || lower == "1") return new JValue(true);
                    if (lower == "false" || lower == "0") return new JValue(false);
                    log.Warning(IssueStage.Load, $"true/false 가 아닙니다: '{text}'",
                        table.SourceFile, table.Name, displayRow, column.Name);
                    return column.Required ? new JValue(false) : JValue.CreateNull();
                }

                case "intarray":
                case "longarray":
                    return ToArray(text, column, log, table, displayRow, ArrayKind.Int);

                case "floatarray":
                    return ToArray(text, column, log, table, displayRow, ArrayKind.Float);

                case "stringarray":
                    return ToArray(text, column, log, table, displayRow, ArrayKind.String);

                // 'array' · 'json' · 모르는 자료형은 ParseColumns 에서 이미 보고했다. 여기서는 string 으로 둔다.
                default:
                    return new JValue(text);
            }
        }

        private enum ArrayKind
        {
            Int,
            Float,
            String
        }

        private static JToken ToArray(
            string text, ColumnSpec column, DataIssueLog log, SourceTable table, int displayRow, ArrayKind kind)
        {
            var array = new JArray();
            foreach (string piece in text.Split(','))
            {
                string item = piece.Trim();
                if (item.Length == 0)
                {
                    continue;
                }

                switch (kind)
                {
                    case ArrayKind.Int:
                        if (long.TryParse(item, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
                        {
                            array.Add(new JValue(l));
                        }
                        else
                        {
                            log.Warning(IssueStage.Load, $"정수 배열 요소를 바꿀 수 없습니다: '{item}' → 0",
                                table.SourceFile, table.Name, displayRow, column.Name);
                            array.Add(new JValue(0L));
                        }
                        break;

                    case ArrayKind.Float:
                        if (double.TryParse(item, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                        {
                            array.Add(FromDouble(d));
                        }
                        else
                        {
                            log.Warning(IssueStage.Load, $"실수 배열 요소를 바꿀 수 없습니다: '{item}' → 0",
                                table.SourceFile, table.Name, displayRow, column.Name);
                            array.Add(new JValue(0d));
                        }
                        break;

                    default:
                        array.Add(new JValue(item));
                        break;
                }
            }
            return array;
        }

        /// <summary>
        /// 소수부가 없으면 정수로 쓴다. 7.0 이 아니라 7 로 나가야 기존 JSON 과 텍스트가 같아진다.
        /// 역직렬화 결과는 어느 쪽이든 동일하다.
        /// </summary>
        private static JToken FromDouble(double value)
        {
            if (Math.Abs(value % 1) < double.Epsilon && Math.Abs(value) <= 9.2e18)
            {
                return new JValue((long)value);
            }
            return new JValue(value);
        }

        /// <summary>
        /// 들여쓰기 4칸, 줄바꿈 LF. 기존 익스포터가 쓰던 형식이라 그대로 맞춘다.
        /// Environment.NewLine 을 그대로 두면 Windows 에서 CRLF 가 섞여 git diff 가 통째로 뜬다.
        /// </summary>
        public static string ToJson(JToken token)
        {
            var writer = new System.IO.StringWriter { NewLine = "\n" };
            using (var json = new Newtonsoft.Json.JsonTextWriter(writer))
            {
                json.Formatting = Newtonsoft.Json.Formatting.Indented;
                json.Indentation = 4;
                json.IndentChar = ' ';
                token.WriteTo(json);
            }
            return writer.ToString();
        }

        private static JToken DefaultOf(string type)
        {
            switch ((type ?? string.Empty).ToLowerInvariant())
            {
                case "int":
                case "integer":
                case "long":
                    return new JValue(0L);
                case "float":
                case "double":
                case "number":
                    return new JValue(0d);
                case "bool":
                case "boolean":
                    return new JValue(false);
                case "intarray":
                case "longarray":
                case "floatarray":
                case "stringarray":
                    return new JArray();
                default:
                    return new JValue(string.Empty);
            }
        }

        /// <summary>
        /// 셀 값을 문자열로. 로더마다 숫자를 double 로 줄 수도, 문자열로 줄 수도 있으므로 여기서 맞춘다.
        /// double 은 왕복 보존되는 "R" 로 찍어 10.5 가 10.500000000000001 로 새지 않게 한다.
        /// </summary>
        public static string CellToText(object cell)
        {
            if (cell == null)
            {
                return string.Empty;
            }
            if (cell is string s)
            {
                return s.Trim();
            }
            if (cell is bool b)
            {
                return b ? "true" : "false";
            }
            if (cell is double d)
            {
                return d.ToString("R", CultureInfo.InvariantCulture);
            }
            if (cell is float f)
            {
                return f.ToString("R", CultureInfo.InvariantCulture);
            }
            if (cell is IFormattable formattable)
            {
                return formattable.ToString(null, CultureInfo.InvariantCulture).Trim();
            }
            return cell.ToString().Trim();
        }
    }
}
