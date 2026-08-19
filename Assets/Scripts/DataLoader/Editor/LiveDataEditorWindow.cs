using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Game_DataLoader
{
    public class LiveDataEditorWindow : EditorWindow
    {
        private const string JsonFolder = "Assets/GameData";
        private const float Splitter = 5f;
        private const float MinPanel = 140f;

        private static readonly Color ChangedBg = new Color(1f, 0.3f, 0.3f, 0.18f);
        private static readonly Color SplitterCol = new Color(0f, 0f, 0f, 0.35f);

        private sealed class TableModel
        {
            public string Name;
            public string Path;
            public JArray Original;
            public JArray Working;
            public string KeyColumn;
            public bool Dirty;
            public bool ParseError;
        }

        private sealed class EditOp
        {
            public string Table;
            public int Row;
            public string Field;
            public JToken Before;
        }

        private readonly Dictionary<string, TableModel> _models = new Dictionary<string, TableModel>();
        private readonly List<EditOp> _undo = new List<EditOp>();
        private List<string> _tableNames = new List<string>();

        private string _selectedTable;
        private int _selectedRow;

        private float _leftW = 220f;
        private float _centerW = 340f;
        private int _dragging;

        private Vector2 _listScroll, _viewScroll, _editScroll;
        private string _search = string.Empty;

        [MenuItem("Tools/GameData/Live Data Editor")]
        private static void Open()
        {
            var win = GetWindow<LiveDataEditorWindow>("Live Data Editor");
            win.minSize = new Vector2(720f, 360f);
        }

        private void OnEnable()
        {
            RefreshTableList();
        }

        private void RefreshTableList()
        {
            _tableNames = new List<string>();
            if (Directory.Exists(JsonFolder) == false)
            {
                return;
            }
            foreach (string path in Directory.GetFiles(JsonFolder, "*.json"))
            {
                _tableNames.Add(Path.GetFileNameWithoutExtension(path));
            }
            _tableNames.Sort(StringComparer.OrdinalIgnoreCase);
        }

        private TableModel EnsureLoaded(string name)
        {
            if (_models.TryGetValue(name, out TableModel m))
            {
                return m;
            }

            string path = Path.Combine(JsonFolder, name + ".json").Replace('\\', '/');
            m = new TableModel { Name = name, Path = path };
            try
            {
                JToken root = JToken.Parse(File.ReadAllText(path));
                m.Original = root as JArray ?? new JArray();
                m.Working = (JArray)m.Original.DeepClone();
                m.KeyColumn = DetectKeyColumn(m.Working);
            }
            catch (Exception e)
            {
                m.ParseError = true;
                m.Original = new JArray();
                m.Working = new JArray();
                Debug.LogWarning($"[LiveDataEditor] '{name}' 로드 실패: {e.Message}");
            }
            _models[name] = m;
            return m;
        }

        private static string DetectKeyColumn(JArray arr)
        {
            if (arr.Count == 0 || (arr[0] is JObject first) == false)
            {
                return null;
            }
            foreach (JProperty p in first.Properties())
            {
                if (string.Equals(p.Name, "id", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.Name, "key", StringComparison.OrdinalIgnoreCase))
                {
                    return p.Name;
                }
            }
            return first.Properties().FirstOrDefault()?.Name;
        }

        private void OnGUI()
        {
            HandleShortcuts();

            float w = position.width;
            float h = position.height;

            _leftW = Mathf.Clamp(_leftW, MinPanel, w - MinPanel * 2 - Splitter * 2);
            _centerW = Mathf.Clamp(_centerW, MinPanel, w - _leftW - MinPanel - Splitter * 2);
            float rightW = w - _leftW - _centerW - Splitter * 2;

            var leftR = new Rect(0, 0, _leftW, h);
            var sp1 = new Rect(_leftW, 0, Splitter, h);
            var centerR = new Rect(_leftW + Splitter, 0, _centerW, h);
            var sp2 = new Rect(_leftW + Splitter + _centerW, 0, Splitter, h);
            var rightR = new Rect(_leftW + Splitter + _centerW + Splitter, 0, rightW, h);

            DrawListPanel(leftR);
            DrawViewPanel(centerR);
            DrawEditPanel(rightR);

            DrawSplitter(sp1, 1);
            DrawSplitter(sp2, 2);
            HandleDrag();
        }

        private void DrawSplitter(Rect r, int id)
        {
            EditorGUI.DrawRect(r, SplitterCol);
            EditorGUIUtility.AddCursorRect(r, MouseCursor.ResizeHorizontal);
            if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
            {
                _dragging = id;
                Event.current.Use();
            }
        }

        private void HandleDrag()
        {
            if (_dragging == 0)
            {
                return;
            }
            if (Event.current.type == EventType.MouseDrag)
            {
                if (_dragging == 1) _leftW += Event.current.delta.x;
                else if (_dragging == 2) _centerW += Event.current.delta.x;
                Repaint();
                Event.current.Use();
            }
            else if (Event.current.type == EventType.MouseUp)
            {
                _dragging = 0;
            }
        }

        private void DrawListPanel(Rect r)
        {
            GUILayout.BeginArea(r, EditorStyles.helpBox);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("JSON Data", EditorStyles.boldLabel);
                if (GUILayout.Button("↻", GUILayout.Width(24)))
                {
                    RefreshTableList();
                }
            }
            EditorGUILayout.Space(2);

            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);
            foreach (string name in _tableNames)
            {
                bool selected = name == _selectedTable;
                TableModel m = _models.TryGetValue(name, out var mm) ? mm : null;
                string label = (m != null && m.Dirty) ? name + " *" : name;

                var prev = GUI.backgroundColor;
                if (selected) GUI.backgroundColor = new Color(0.4f, 0.6f, 1f);
                if (GUILayout.Button(label, EditorStyles.miniButton))
                {
                    _selectedTable = name;
                    _selectedRow = 0;
                    EnsureLoaded(name);
                }
                GUI.backgroundColor = prev;
            }
            EditorGUILayout.EndScrollView();

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(Application.isPlaying ? "▶ Play (런타임 반영)" : "■ Edit (파일만)",
                EditorStyles.miniLabel);
            GUILayout.EndArea();
        }

        private TableModel GetSelected(out int rowCount)
        {
            rowCount = 0;
            if (string.IsNullOrEmpty(_selectedTable)) return null;
            TableModel m = EnsureLoaded(_selectedTable);
            rowCount = m.Working.Count;
            if (rowCount > 0) _selectedRow = Mathf.Clamp(_selectedRow, 0, rowCount - 1);
            return m;
        }

        private void DrawViewPanel(Rect r)
        {
            GUILayout.BeginArea(r, EditorStyles.helpBox);
            TableModel m = GetSelected(out int rowCount);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(m == null ? "보기" : $"보기 — {m.Name}", EditorStyles.boldLabel);
                GUI.enabled = m != null && m.Dirty;
                if (GUILayout.Button("저장", GUILayout.Width(56)))
                {
                    SaveTable(m);
                }
                GUI.enabled = true;
            }

            if (m == null) { EditorGUILayout.HelpBox("좌측에서 테이블을 선택하세요.", MessageType.Info); GUILayout.EndArea(); return; }
            if (m.ParseError) { EditorGUILayout.HelpBox("JSON 파싱 실패.", MessageType.Error); GUILayout.EndArea(); return; }
            if (rowCount == 0) { EditorGUILayout.HelpBox("행이 없습니다.", MessageType.Info); GUILayout.EndArea(); return; }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("검색", GUILayout.Width(32));
                _search = EditorGUILayout.TextField(_search ?? string.Empty);
                if (GUILayout.Button("✕", GUILayout.Width(22)))
                {
                    _search = string.Empty;
                    GUI.FocusControl(null);
                }
            }
            EditorGUILayout.Space(2);

            _viewScroll = EditorGUILayout.BeginScrollView(_viewScroll);
            string q = (_search ?? string.Empty).Trim();
            int shown = 0;
            for (int i = 0; i < rowCount; i++)
            {
                if ((m.Working[i] is JObject row) == false) continue;
                if (MatchRow(row, q) == false) continue;
                shown++;
                DrawRowGroup(m, i, row);
            }
            if (shown == 0) EditorGUILayout.LabelField("검색 결과 없음", EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawRowGroup(TableModel m, int index, JObject row)
        {
            bool selected = index == _selectedRow;

            Color prevBg = GUI.backgroundColor;
            if (selected) GUI.backgroundColor = new Color(0.4f, 0.6f, 1f, 1f);
            Rect groupRect = EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = prevBg;

            string keyText = (m.KeyColumn != null && row[m.KeyColumn] != null)
                ? $"{m.KeyColumn} : {row[m.KeyColumn]}"
                : $"[{index}]";
            EditorGUILayout.LabelField(keyText, EditorStyles.boldLabel);

            JObject orig = index < m.Original.Count ? m.Original[index] as JObject : null;
            foreach (JProperty p in row.Properties())
            {
                JToken cur = p.Value;
                JToken before = orig?[p.Name];
                bool changed = before != null && !JToken.DeepEquals(before, cur);

                Rect line = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                if (changed) EditorGUI.DrawRect(line, ChangedBg);
                string text = changed
                    ? $"{TokenToDisplay(before)}  →  {TokenToDisplay(cur)}"
                    : TokenToDisplay(cur);
                EditorGUI.LabelField(line, p.Name, text);
            }

            EditorGUILayout.EndVertical();

            EditorGUIUtility.AddCursorRect(groupRect, MouseCursor.Link);
            if (Event.current.type == EventType.MouseDown && groupRect.Contains(Event.current.mousePosition))
            {
                _selectedRow = index;
                GUI.FocusControl(null);
                Repaint();
                Event.current.Use();
            }

            EditorGUILayout.Space(3);
            Rect sep = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(sep, SplitterCol);
            EditorGUILayout.Space(3);
        }

        private static bool MatchRow(JObject row, string q)
        {
            if (string.IsNullOrEmpty(q)) return true;
            q = q.ToLowerInvariant();
            foreach (JProperty p in row.Properties())
            {
                if (p.Value.ToString().ToLowerInvariant().Contains(q)) return true;
            }
            return false;
        }

        private void DrawEditPanel(Rect r)
        {
            GUILayout.BeginArea(r, EditorStyles.helpBox);
            TableModel m = GetSelected(out int rowCount);

            EditorGUILayout.LabelField(m == null ? "편집" : $"편집 — {m.Name}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Ctrl+Z 되돌리기 · Ctrl+C/V 값 복사/붙여넣기", EditorStyles.miniLabel);

            if (m == null || m.ParseError || rowCount == 0) { GUILayout.EndArea(); return; }

            EditorGUILayout.Space(2);
            _editScroll = EditorGUILayout.BeginScrollView(_editScroll);
            var work = (JObject)m.Working[_selectedRow];

            foreach (JProperty p in work.Properties().ToList())
            {
                DrawEditableField(m, _selectedRow, p);
            }
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawEditableField(TableModel m, int row, JProperty p)
        {
            JToken val = p.Value;
            string field = p.Name;
            GUI.SetNextControlName($"edit::{m.Name}::{row}::{field}");

            switch (val.Type)
            {
                case JTokenType.Boolean:
                {
                    bool b = val.Value<bool>();
                    bool nb = EditorGUILayout.Toggle(field, b);
                    if (nb != b) CommitEdit(m, row, field, new JValue(nb));
                    break;
                }
                case JTokenType.Integer:
                {
                    long v = val.Value<long>();
                    string s = EditorGUILayout.DelayedTextField(field, v.ToString(CultureInfo.InvariantCulture));
                    if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long nv))
                    {
                        if (nv != v) CommitEdit(m, row, field, new JValue(nv));
                    }

                    else if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double nd))
                    {
                        CommitEdit(m, row, field, new JValue(nd));
                    }
                    break;
                }
                case JTokenType.Float:
                {
                    double v = val.Value<double>();
                    string s = EditorGUILayout.DelayedTextField(field, v.ToString(CultureInfo.InvariantCulture));
                    if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double nv) && !nv.Equals(v))
                        CommitEdit(m, row, field, new JValue(nv));
                    break;
                }
                case JTokenType.String:
                {
                    string v = val.Value<string>() ?? string.Empty;
                    string s = EditorGUILayout.DelayedTextField(field, v);
                    if (s != v) CommitEdit(m, row, field, new JValue(s));
                    break;
                }
                case JTokenType.Array:
                {
                    string v = ArrayToCsv((JArray)val);
                    string s = EditorGUILayout.DelayedTextField(field + "  [ , ]", v);
                    if (s != v) CommitEdit(m, row, field, CsvToArray(s));
                    break;
                }
                case JTokenType.Null:
                {
                    string s = EditorGUILayout.DelayedTextField(field + "  (null)", string.Empty);
                    if (!string.IsNullOrEmpty(s)) CommitEdit(m, row, field, new JValue(s));
                    break;
                }
                default:
                {
                    string v = val.ToString(Formatting.None);
                    string s = EditorGUILayout.DelayedTextField(field + "  (json)", v);
                    if (s != v)
                    {
                        try { CommitEdit(m, row, field, JToken.Parse(s)); }
                        catch { }
                    }
                    break;
                }
            }
        }

        private void CommitEdit(TableModel m, int row, string field, JToken newVal)
        {
            var obj = (JObject)m.Working[row];
            JToken old = obj[field];
            if (JToken.DeepEquals(old, newVal)) return;

            _undo.Add(new EditOp { Table = m.Name, Row = row, Field = field, Before = old?.DeepClone() });
            obj[field] = newVal;
            RecomputeDirty(m);
            ApplyToRuntime(m, row, field, newVal);
            Repaint();
        }

        private void RecomputeDirty(TableModel m)
        {
            m.Dirty = !JToken.DeepEquals(m.Original, m.Working);
        }

        private void ApplyToRuntime(TableModel m, int row, string field, JToken val)
        {
            if (!Application.isPlaying) return;

            IDataContainer container = DataManager.Instance.GetContainerByName(m.Name);
            if (container == null) return;

            string keyStr = (m.Working[row] as JObject)?[m.KeyColumn]?.ToString();
            object target = FindRuntimeObject(container, keyStr);
            if (target == null) return;

            PropertyInfo prop = FindPropByJsonName(target.GetType(), field);
            MethodInfo setter = prop?.GetSetMethod(true);
            if (setter == null) return;

            try
            {
                object converted = val.ToObject(prop.PropertyType);
                prop.SetValue(target, converted);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LiveDataEditor] 런타임 반영 실패 {m.Name}.{field}: {e.Message}");
            }
        }

        private static object FindRuntimeObject(IDataContainer container, string keyStr)
        {
            if (string.IsNullOrEmpty(keyStr)) return null;

            Type ct = container.GetType();
            PropertyInfo coll = ct.GetProperty("AllValues") ?? ct.GetProperty("All");
            if ((coll?.GetValue(container) is IEnumerable items) == false) return null;

            foreach (object item in items)
            {
                if (item == null) continue;
                object value = item;
                Type it = item.GetType();
                if (it.IsGenericType && it.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                {
                    value = it.GetProperty("Value")?.GetValue(item);
                }
                if (value == null) continue;

                PropertyInfo keyProp = value.GetType().GetProperty("Key");
                object k = keyProp?.GetValue(value);
                if (k != null && k.ToString() == keyStr) return value;
            }
            return null;
        }

        private static PropertyInfo FindPropByJsonName(Type t, string jsonName)
        {
            PropertyInfo[] props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (PropertyInfo p in props)
            {
                if (p.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
                var attr = p.GetCustomAttribute<JsonPropertyAttribute>();
                string name = (attr != null && !string.IsNullOrEmpty(attr.PropertyName)) ? attr.PropertyName : p.Name;
                if (name == jsonName) return p;
            }
            foreach (PropertyInfo p in props)
            {
                if (string.Equals(p.Name, jsonName, StringComparison.OrdinalIgnoreCase)) return p;
            }
            return null;
        }

        private void HandleShortcuts()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown) return;
            if (!(e.control || e.command)) return;

            if (e.keyCode == KeyCode.Z) { Undo(); e.Use(); }
            else if (e.keyCode == KeyCode.C) { if (CopyFocused()) e.Use(); }
            else if (e.keyCode == KeyCode.V) { if (PasteFocused()) e.Use(); }
        }

        private void Undo()
        {
            if (_undo.Count == 0) return;
            EditOp op = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);

            if (!_models.TryGetValue(op.Table, out TableModel m)) return;
            if (op.Row >= m.Working.Count) return;

            var obj = (JObject)m.Working[op.Row];
            obj[op.Field] = op.Before ?? JValue.CreateNull();
            RecomputeDirty(m);

            _selectedTable = op.Table;
            _selectedRow = op.Row;
            ApplyToRuntime(m, op.Row, op.Field, obj[op.Field]);
            Repaint();
        }

        private bool TryParseFocused(out TableModel m, out int row, out string field)
        {
            m = null; row = -1; field = null;
            string name = GUI.GetNameOfFocusedControl();
            if (string.IsNullOrEmpty(name) || !name.StartsWith("edit::")) return false;

            string[] parts = name.Split(new[] { "::" }, StringSplitOptions.None);
            if (parts.Length != 4) return false;
            if (!_models.TryGetValue(parts[1], out m)) return false;
            if (!int.TryParse(parts[2], out row)) return false;
            field = parts[3];
            return row >= 0 && row < m.Working.Count;
        }

        private bool CopyFocused()
        {
            if (!TryParseFocused(out TableModel m, out int row, out string field)) return false;
            JToken v = ((JObject)m.Working[row])[field];
            EditorGUIUtility.systemCopyBuffer = TokenToEditString(v);
            return true;
        }

        private bool PasteFocused()
        {
            if (!TryParseFocused(out TableModel m, out int row, out string field)) return false;
            JToken cur = ((JObject)m.Working[row])[field];
            JToken nv = ParseEditString(EditorGUIUtility.systemCopyBuffer, cur);
            CommitEdit(m, row, field, nv);
            return true;
        }

        private void SaveTable(TableModel m)
        {
            if (m == null || !m.Dirty) return;
            try
            {
                File.WriteAllText(m.Path, m.Working.ToString(Formatting.Indented));
                m.Original = (JArray)m.Working.DeepClone();
                m.Dirty = false;
                AssetDatabase.Refresh();
                Debug.Log($"[LiveDataEditor] 저장: {m.Path}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[LiveDataEditor] 저장 실패 {m.Path}: {e.Message}");
            }
        }

        private void SaveAllDirty()
        {
            foreach (TableModel m in _models.Values)
            {
                if (m.Dirty) SaveTable(m);
            }
        }

        private bool HasDirty()
        {
            foreach (TableModel m in _models.Values)
            {
                if (m.Dirty) return true;
            }
            return false;
        }

        private void OnDestroy()
        {
            if (!HasDirty()) return;

            bool save = EditorUtility.DisplayDialog(
                "변경 사항 저장",
                "변경한 값을 JSON 파일에 저장하시겠습니까?",
                "네", "아니요");

            if (save) SaveAllDirty();
        }

        private static string TokenToDisplay(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null) return "null";
            if (t.Type == JTokenType.Array) return "[" + ArrayToCsv((JArray)t) + "]";
            return t.ToString();
        }

        private static string TokenToEditString(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null) return string.Empty;
            if (t.Type == JTokenType.Array) return ArrayToCsv((JArray)t);
            if (t.Type == JTokenType.String) return t.Value<string>() ?? string.Empty;
            return t.ToString();
        }

        private static JToken ParseEditString(string s, JToken like)
        {
            s = s ?? string.Empty;
            switch (like?.Type)
            {
                case JTokenType.Boolean:
                    return new JValue(bool.TryParse(s, out bool b) && b);
                case JTokenType.Integer:
                    if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l)) return new JValue(l);
                    return new JValue(double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double dv) ? dv : 0d);
                case JTokenType.Float:
                    return new JValue(double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0d);
                case JTokenType.Array:
                    return CsvToArray(s);
                default:
                    return new JValue(s);
            }
        }

        private static string ArrayToCsv(JArray a)
        {
            return string.Join(",", a.Select(t => t.ToString()));
        }

        private static JArray CsvToArray(string s)
        {
            var arr = new JArray();
            if (string.IsNullOrWhiteSpace(s)) return arr;

            foreach (string raw in s.Split(','))
            {
                string tok = raw.Trim();
                if (tok.Length == 0) continue;

                if (long.TryParse(tok, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
                    arr.Add(new JValue(l));
                else if (double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    arr.Add(new JValue(d));
                else
                    arr.Add(new JValue(tok));
            }
            return arr;
        }
    }
}
