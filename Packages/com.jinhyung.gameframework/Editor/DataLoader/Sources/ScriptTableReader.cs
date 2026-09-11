using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game_DataLoader
{
    /// <summary>
    /// 스크립트 포맷(js·ts·py)이 내보낸 표 묶음을 SourceTable 로 바꾼다.
    ///
    ///   { "HeroData": [[컬럼명..], [자료형..], [값..]] }        일반 표
    ///   { "_Enum": { "StatType": ["Attack", "Defense"] } }      enum 정의
    ///
    /// enum 쪽은 배열이 아니라 객체로 쓰는 편이 자연스러워서, 여기서 헤더=이름·아래=멤버인
    /// 표 모양으로 눕혀준다. 그러면 아래 파이프라인이 엑셀과 똑같이 다룬다.
    /// </summary>
    public static class ScriptTableReader
    {
        public static List<SourceTable> FromJson(string json, string fileName, DataIssueLog log)
        {
            var tables = new List<SourceTable>();

            if (string.IsNullOrEmpty(json) || json.Trim().Length == 0)
            {
                log.Error(IssueStage.Load, "출력이 비어 있습니다.", fileName);
                return tables;
            }

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException e)
            {
                log.Error(IssueStage.Load, $"결과를 JSON 으로 읽지 못했습니다: {e.Message}", fileName);
                return tables;
            }

            foreach (JProperty property in root.Properties())
            {
                SourceTable table = ToTable(property.Name, property.Value, fileName, log);
                if (table != null)
                {
                    tables.Add(table);
                }
            }

            if (tables.Count == 0)
            {
                log.Warning(IssueStage.Load, "표를 하나도 찾지 못했습니다.", fileName);
            }
            return tables;
        }

        private static SourceTable ToTable(string name, JToken value, string fileName, DataIssueLog log)
        {
            var table = new SourceTable { Name = name, SourceFile = fileName };

            if (value is JArray rows)
            {
                foreach (JToken row in rows)
                {
                    var cells = new List<object>();
                    if (row is JArray cellArray)
                    {
                        foreach (JToken cell in cellArray)
                        {
                            cells.Add(ToCell(cell));
                        }
                    }
                    else
                    {
                        cells.Add(ToCell(row));
                    }
                    table.Rows.Add(cells);
                }
                return table;
            }

            if (value is JObject grouped)
            {
                // enum 규약: 키가 열 머리, 값 배열이 그 아래로 내려간다.
                var names = new List<string>();
                var columns = new List<List<object>>();
                int height = 0;

                foreach (JProperty item in grouped.Properties())
                {
                    names.Add(item.Name);
                    var members = new List<object>();
                    if (item.Value is JArray array)
                    {
                        foreach (JToken member in array)
                        {
                            members.Add(ToCell(member));
                        }
                    }
                    columns.Add(members);
                    height = Math.Max(height, members.Count);
                }

                var header = new List<object>();
                foreach (string columnName in names)
                {
                    header.Add(columnName);
                }
                table.Rows.Add(header);

                for (int r = 0; r < height; r++)
                {
                    var line = new List<object>();
                    foreach (List<object> column in columns)
                    {
                        line.Add(r < column.Count ? column[r] : null);
                    }
                    table.Rows.Add(line);
                }
                return table;
            }

            log.Error(IssueStage.Load,
                $"'{name}' 의 값이 배열도 객체도 아닙니다. 표는 행 배열로, enum 은 객체로 내보내야 합니다.",
                fileName, name);
            return null;
        }

        private static object ToCell(JToken token)
        {
            if (token == null)
            {
                return null;
            }

            switch (token.Type)
            {
                case JTokenType.Null:
                case JTokenType.Undefined:
                    return null;
                case JTokenType.Boolean:
                    return token.Value<bool>();
                case JTokenType.Integer:
                    return (double)token.Value<long>();
                case JTokenType.Float:
                    return token.Value<double>();
                default:
                    return token.ToString();
            }
        }
    }
}
