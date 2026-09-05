using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Game_DataLoader
{
    /// <summary>
    /// .xlsx 를 외부 라이브러리 없이 읽는다.
    /// xlsx 는 ZIP 안의 XML 이라 표준 라이브러리만으로 셀 값을 꺼낼 수 있다.
    /// 서식·수식·차트는 다루지 않는다. 필요한 것은 셀에 보이는 값뿐이다.
    /// </summary>
    public sealed class XlsxSourceLoader : IDataSourceLoader
    {
        public string Extension => ".xlsx";
        public string DisplayName => "Excel";
        public int Order => 10;

        public bool IsAvailable(out string reason)
        {
            reason = null;
            return true;
        }

        public IEnumerable<SourceTable> Load(string filePath, DataIssueLog log)
        {
            var tables = new List<SourceTable>();
            string fileName = Path.GetFileName(filePath);

            ZipArchive zip;
            try
            {
                zip = ZipFile.OpenRead(filePath);
            }
            catch (Exception e)
            {
                log.Error(IssueStage.Load, $"파일을 열 수 없습니다: {e.Message}", fileName);
                return tables;
            }

            using (zip)
            {
                try
                {
                    List<string> sharedStrings = ReadSharedStrings(zip);
                    foreach (SheetRef sheet in ReadSheetRefs(zip, fileName, log))
                    {
                        ZipArchiveEntry entry = zip.GetEntry(sheet.Path);
                        if (entry == null)
                        {
                            log.Warning(IssueStage.Load, $"시트 XML 을 찾지 못했습니다: {sheet.Path}", fileName, sheet.Name);
                            continue;
                        }

                        SourceTable table = ReadSheet(entry, sheet.Name, fileName, sharedStrings);
                        if (table.Rows.Count > 0)
                        {
                            tables.Add(table);
                        }
                    }
                }
                catch (Exception e)
                {
                    log.Error(IssueStage.Load, $"읽는 중 오류: {e.GetType().Name} {e.Message}", fileName);
                }
            }

            return tables;
        }

        private struct SheetRef
        {
            public string Name;
            public string Path;
        }

        /// <summary>
        /// workbook.xml 의 시트 순서와 workbook.xml.rels 의 대상 경로를 맞춘다.
        /// r:id 로 이어져 있으므로 둘을 다 읽어야 시트 이름과 파일이 짝지어진다.
        /// </summary>
        private static List<SheetRef> ReadSheetRefs(ZipArchive zip, string fileName, DataIssueLog log)
        {
            var result = new List<SheetRef>();

            ZipArchiveEntry workbook = zip.GetEntry("xl/workbook.xml");
            if (workbook == null)
            {
                log.Error(IssueStage.Load, "xl/workbook.xml 이 없습니다. 올바른 xlsx 가 아닙니다.", fileName);
                return result;
            }

            var relations = new Dictionary<string, string>();
            ZipArchiveEntry rels = zip.GetEntry("xl/_rels/workbook.xml.rels");
            if (rels != null)
            {
                using (Stream stream = rels.Open())
                using (XmlReader reader = CreateReader(stream))
                {
                    while (reader.Read())
                    {
                        if (reader.NodeType != XmlNodeType.Element || reader.Name != "Relationship")
                        {
                            continue;
                        }
                        string id = reader.GetAttribute("Id");
                        string target = reader.GetAttribute("Target");
                        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(target))
                        {
                            continue;
                        }
                        relations[id] = NormalizeTarget(target);
                    }
                }
            }

            using (Stream stream = workbook.Open())
            using (XmlReader reader = CreateReader(stream))
            {
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element || reader.Name != "sheet")
                    {
                        continue;
                    }

                    string name = reader.GetAttribute("name");
                    string rid = reader.GetAttribute("r:id") ?? reader.GetAttribute("id");
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    string path = null;
                    if (string.IsNullOrEmpty(rid) == false && relations.TryGetValue(rid, out string mapped))
                    {
                        path = mapped;
                    }
                    if (string.IsNullOrEmpty(path))
                    {
                        path = "xl/worksheets/sheet" + (result.Count + 1) + ".xml";
                    }

                    result.Add(new SheetRef { Name = name, Path = path });
                }
            }

            return result;
        }

        private static string NormalizeTarget(string target)
        {
            string value = target.Replace('\\', '/');
            if (value.StartsWith("/"))
            {
                return value.TrimStart('/');
            }
            if (value.StartsWith("xl/"))
            {
                return value;
            }
            return "xl/" + value;
        }

        private static List<string> ReadSharedStrings(ZipArchive zip)
        {
            var result = new List<string>();
            ZipArchiveEntry entry = zip.GetEntry("xl/sharedStrings.xml");
            if (entry == null)
            {
                return result;
            }

            using (Stream stream = entry.Open())
            using (XmlReader reader = CreateReader(stream))
            {
                var sb = new StringBuilder();
                bool inItem = false;
                bool skipRuby = false;

                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        if (reader.Name == "si")
                        {
                            inItem = true;
                            skipRuby = false;
                            sb.Length = 0;
                        }
                        else if (reader.Name == "rPh")
                        {
                            // 후리가나(발음 표기). 본문이 아니므로 건너뛴다.
                            skipRuby = true;
                        }
                        else if (reader.Name == "t" && inItem && skipRuby == false)
                        {
                            sb.Append(reader.ReadElementContentAsString());
                        }
                    }
                    else if (reader.NodeType == XmlNodeType.EndElement)
                    {
                        if (reader.Name == "rPh")
                        {
                            skipRuby = false;
                        }
                        else if (reader.Name == "si")
                        {
                            result.Add(sb.ToString());
                            inItem = false;
                        }
                    }
                }
            }

            return result;
        }

        private static SourceTable ReadSheet(ZipArchiveEntry entry, string sheetName, string fileName, List<string> sharedStrings)
        {
            var table = new SourceTable { Name = sheetName, SourceFile = fileName };
            var rows = new List<List<object>>();

            using (Stream stream = entry.Open())
            using (XmlReader reader = CreateReader(stream))
            {
                List<object> current = null;
                int currentRowIndex = -1;

                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element && reader.Name == "row")
                    {
                        current = new List<object>();
                        currentRowIndex = ParseInt(reader.GetAttribute("r"), rows.Count + 1) - 1;
                        if (reader.IsEmptyElement)
                        {
                            PlaceRow(rows, currentRowIndex, current);
                            current = null;
                        }
                        continue;
                    }

                    if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "row")
                    {
                        PlaceRow(rows, currentRowIndex, current);
                        current = null;
                        continue;
                    }

                    if (current == null || reader.NodeType != XmlNodeType.Element || reader.Name != "c")
                    {
                        continue;
                    }

                    int columnIndex = ColumnIndexOf(reader.GetAttribute("r"), current.Count);
                    string cellType = reader.GetAttribute("t");
                    object value = ReadCell(reader, cellType, sharedStrings);

                    while (current.Count < columnIndex)
                    {
                        current.Add(null);
                    }
                    if (current.Count == columnIndex)
                    {
                        current.Add(value);
                    }
                    else
                    {
                        current[columnIndex] = value;
                    }
                }

                if (current != null)
                {
                    PlaceRow(rows, currentRowIndex, current);
                }
            }

            foreach (List<object> row in rows)
            {
                table.Rows.Add(row);
            }
            return table;
        }

        private static void PlaceRow(List<List<object>> rows, int index, List<object> row)
        {
            if (row == null)
            {
                return;
            }
            if (index < 0)
            {
                index = rows.Count;
            }
            while (rows.Count < index)
            {
                rows.Add(new List<object>());
            }
            if (rows.Count == index)
            {
                rows.Add(row);
            }
            else
            {
                rows[index] = row;
            }
        }

        private static object ReadCell(XmlReader reader, string cellType, List<string> sharedStrings)
        {
            string raw = null;
            bool inlineString = false;
            var inlineText = new StringBuilder();

            // ReadElementContentAsString 은 </c> 를 지나치므로, 바깥 루프와 위치가 어긋난다.
            // 서브트리로 가둬 이 셀 범위만 읽고 바깥 reader 는 </c> 에 남게 한다.
            using (XmlReader cell = reader.ReadSubtree())
            {
                cell.Read();
                while (cell.Read())
                {
                    if (cell.NodeType != XmlNodeType.Element)
                    {
                        continue;
                    }

                    if (cell.Name == "v")
                    {
                        raw = cell.ReadElementContentAsString();
                    }
                    else if (cell.Name == "is")
                    {
                        inlineString = true;
                    }
                    else if (cell.Name == "t" && inlineString)
                    {
                        inlineText.Append(cell.ReadElementContentAsString());
                    }
                }
            }

            if (inlineString)
            {
                return inlineText.ToString();
            }
            if (raw == null)
            {
                return null;
            }

            switch (cellType)
            {
                case "s":
                {
                    int index = ParseInt(raw, -1);
                    return (index >= 0 && index < sharedStrings.Count) ? sharedStrings[index] : raw;
                }
                case "b":
                    return raw == "1";
                case "str":
                case "inlineStr":
                    return raw;
                case "e":
                    // 수식 오류(#REF! 등). 문자열로 넘겨 이후 자료형 변환에서 걸리게 둔다.
                    return raw;
                default:
                {
                    if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                    {
                        return number;
                    }
                    return raw;
                }
            }
        }

        /// <summary>"B3" 처럼 생긴 셀 참조에서 열 인덱스(0부터)를 얻는다.</summary>
        private static int ColumnIndexOf(string reference, int fallback)
        {
            if (string.IsNullOrEmpty(reference))
            {
                return fallback;
            }

            int index = 0;
            int letters = 0;
            foreach (char c in reference)
            {
                if (c >= 'A' && c <= 'Z')
                {
                    index = index * 26 + (c - 'A' + 1);
                    letters++;
                }
                else if (c >= 'a' && c <= 'z')
                {
                    index = index * 26 + (c - 'a' + 1);
                    letters++;
                }
                else
                {
                    break;
                }
            }

            return letters == 0 ? fallback : index - 1;
        }

        private static int ParseInt(string value, int fallback)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : fallback;
        }

        private static XmlReader CreateReader(Stream stream)
        {
            var settings = new XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreWhitespace = false,
                IgnoreProcessingInstructions = true,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };
            return XmlReader.Create(stream, settings);
        }
    }
}
