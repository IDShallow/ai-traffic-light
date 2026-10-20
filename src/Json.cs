using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Web.Script.Serialization;

namespace ClaudeTrafficLight
{
    /// <summary>JSON 读写。只依赖 .NET Framework 自带的 JavaScriptSerializer（无第三方库）。</summary>
    internal static class Json
    {
        private static readonly JavaScriptSerializer Ser = CreateSerializer();

        private static JavaScriptSerializer CreateSerializer()
        {
            JavaScriptSerializer s = new JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue;
            s.RecursionLimit = 200;
            return s;
        }

        public static Dictionary<string, object> ParseObject(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            try
            {
                object o = Ser.DeserializeObject(text);
                return o as Dictionary<string, object>;
            }
            catch
            {
                return null;
            }
        }

        public static object Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            try { return Ser.DeserializeObject(text); }
            catch { return null; }
        }

        public static string Stringify(object o)
        {
            try { return Ser.Serialize(o); }
            catch { return "{}"; }
        }

        public static string Str(Dictionary<string, object> d, string key)
        {
            if (d == null || !d.ContainsKey(key)) return "";
            object v = d[key];
            if (v == null) return "";
            return v as string ?? Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static long Long(Dictionary<string, object> d, string key, long def)
        {
            if (d == null || !d.ContainsKey(key)) return def;
            object v = d[key];
            if (v == null) return def;
            try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); }
            catch { return def; }
        }

        public static bool Bool(Dictionary<string, object> d, string key, bool def)
        {
            if (d == null || !d.ContainsKey(key)) return def;
            object v = d[key];
            if (v is bool) return (bool)v;
            if (v is string) return string.Equals((string)v, "true", StringComparison.OrdinalIgnoreCase);
            return def;
        }

        public static Dictionary<string, object> Map(Dictionary<string, object> d, string key)
        {
            if (d == null || !d.ContainsKey(key)) return null;
            return d[key] as Dictionary<string, object>;
        }

        public static object[] List(Dictionary<string, object> d, string key)
        {
            if (d == null || !d.ContainsKey(key)) return null;
            return d[key] as object[];
        }

        // ---------- 带缩进的输出（保持用户手改的 settings.json 可读） ----------

        public static string Pretty(object o)
        {
            StringBuilder sb = new StringBuilder();
            WriteValue(sb, o, 0);
            sb.Append('\n');
            return sb.ToString();
        }

        private static void Indent(StringBuilder sb, int n)
        {
            for (int i = 0; i < n; i++) sb.Append("  ");
        }

        private static void WriteValue(StringBuilder sb, object o, int indent)
        {
            if (o == null) { sb.Append("null"); return; }

            string s = o as string;
            if (s != null) { sb.Append(Ser.Serialize(s)); return; }

            if (o is bool) { sb.Append(((bool)o) ? "true" : "false"); return; }

            if (o is int || o is long || o is double || o is float || o is decimal)
            {
                sb.Append(Convert.ToString(o, CultureInfo.InvariantCulture));
                return;
            }

            IDictionary dict = o as IDictionary;
            if (dict != null)
            {
                if (dict.Count == 0) { sb.Append("{}"); return; }
                sb.Append("{\n");
                int i = 0;
                foreach (DictionaryEntry e in dict)
                {
                    Indent(sb, indent + 1);
                    sb.Append(Ser.Serialize(Convert.ToString(e.Key, CultureInfo.InvariantCulture)));
                    sb.Append(": ");
                    WriteValue(sb, e.Value, indent + 1);
                    if (++i < dict.Count) sb.Append(',');
                    sb.Append('\n');
                }
                Indent(sb, indent);
                sb.Append('}');
                return;
            }

            IEnumerable arr = o as IEnumerable;
            if (arr != null)
            {
                List<object> items = new List<object>();
                foreach (object item in arr) items.Add(item);
                if (items.Count == 0) { sb.Append("[]"); return; }
                sb.Append("[\n");
                for (int i = 0; i < items.Count; i++)
                {
                    Indent(sb, indent + 1);
                    WriteValue(sb, items[i], indent + 1);
                    if (i < items.Count - 1) sb.Append(',');
                    sb.Append('\n');
                }
                Indent(sb, indent);
                sb.Append(']');
                return;
            }

            sb.Append(Ser.Serialize(o));
        }
    }
}
