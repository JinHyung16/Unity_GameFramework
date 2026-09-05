using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Game_DataLoader
{
    /// <summary>
    /// .csv 표를 읽는다. 파일 이름이 곧 표 이름이다.
    /// 따옴표로 감싼 값, 그 안의 쉼표·줄바꿈, 두 겹 따옴표("")를 처리한다.
    /// </summary>
    public sealed class CsvSourceLoader : IDataSourceLoader
    {
        public string Extension => ".csv";
        public string DisplayName => "CSV";
        public int Order => 15;

        public bool IsAvailable(out string reason)
        {
            reason = null;
            return true;
        }

        public IEnumerable<SourceTable> Load(string filePath, DataIssueLog log)
        {
            var tables = new List<SourceTable>();
            string fileName = Path.GetFileName(filePath);

            string text;
            try
            {
                text = File.ReadAllText(filePath, Encoding.UTF8);
            }
            catch (System.Exception e)
            {
                log.Error(IssueStage.Load, $"파일을 읽을 수 없습니다: {e.Message}", fileName);
                return tables;
            }

            var table = new SourceTable
            {
                Name = Path.GetFileNameWithoutExtension(filePath),
                SourceFile = fileName
            };

            foreach (List<object> row in Parse(text))
            {
                table.Rows.Add(row);
            }

            if (table.Rows.Count > 0)
            {
                tables.Add(table);
            }
            return tables;
        }

        private static List<List<object>> Parse(string text)
        {
            var rows = new List<List<object>>();
            var row = new List<object>();
            var field = new StringBuilder();
            bool quoted = false;
            bool fieldStarted = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (quoted)
                {
                    if (c == '"')
                    {
                        // "" 는 따옴표 한 글자를 뜻한다.
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            quoted = false;
                        }
                    }
                    else
                    {
                        field.Append(c);
                    }
                    continue;
                }

                if (c == '"' && fieldStarted == false)
                {
                    quoted = true;
                    fieldStarted = true;
                    continue;
                }

                if (c == ',')
                {
                    row.Add(field.ToString());
                    field.Length = 0;
                    fieldStarted = false;
                    continue;
                }

                if (c == '\r')
                {
                    continue;
                }

                if (c == '\n')
                {
                    row.Add(field.ToString());
                    field.Length = 0;
                    fieldStarted = false;
                    rows.Add(row);
                    row = new List<object>();
                    continue;
                }

                field.Append(c);
                fieldStarted = true;
            }

            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row);
            }

            // BOM 이 첫 셀에 붙어 들어오면 열 이름이 어긋난다.
            if (rows.Count > 0 && rows[0].Count > 0 && rows[0][0] is string first && first.Length > 0 && first[0] == '﻿')
            {
                rows[0][0] = first.Substring(1);
            }

            return rows;
        }
    }
}
