// 최소 JSON 파서 + 리플렉션 매퍼. UnityEngine 의존 없음 (테스트와 게임이 같은 설정 파일을 읽는다).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace LabMeat.Sim
{
    public static class Json
    {
        public static T Parse<T>(string text) where T : new()
        {
            int i = 0;
            object root = ParseValue(text, ref i);
            return (T)Map(root, typeof(T));
        }

        // ---------- 파서 ----------

        static void Skip(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
                break;
            }
        }

        static object ParseValue(string s, ref int i)
        {
            Skip(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON: 예기치 않은 끝");
            char c = s[i];
            if (c == '{') return ParseObject(s, ref i);
            if (c == '[') return ParseArray(s, ref i);
            if (c == '"') return ParseString(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            return ParseNumber(s, ref i);
        }

        static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var d = new Dictionary<string, object>();
            i++;
            Skip(s, ref i);
            if (s[i] == '}') { i++; return d; }
            while (true)
            {
                Skip(s, ref i);
                string k = ParseString(s, ref i);
                Skip(s, ref i);
                if (s[i] != ':') throw new FormatException("JSON: ':' 필요 @" + i);
                i++;
                d[k] = ParseValue(s, ref i);
                Skip(s, ref i);
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return d; }
                throw new FormatException("JSON: ',' 또는 '}' 필요 @" + i);
            }
        }

        static List<object> ParseArray(string s, ref int i)
        {
            var l = new List<object>();
            i++;
            Skip(s, ref i);
            if (s[i] == ']') { i++; return l; }
            while (true)
            {
                l.Add(ParseValue(s, ref i));
                Skip(s, ref i);
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return l; }
                throw new FormatException("JSON: ',' 또는 ']' 필요 @" + i);
            }
        }

        static string ParseString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("JSON: 문자열 필요 @" + i);
            i++;
            var sb = new StringBuilder();
            while (s[i] != '"')
            {
                char c = s[i++];
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            i++;
            return sb.ToString();
        }

        static double ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
        }

        // ---------- 매퍼 ----------

        static object Map(object v, Type t)
        {
            if (v == null) return t.IsValueType ? Activator.CreateInstance(t) : null;
            if (t == typeof(string)) return v.ToString();
            if (t == typeof(int)) return Convert.ToInt32(v, CultureInfo.InvariantCulture);
            if (t == typeof(float)) return Convert.ToSingle(v, CultureInfo.InvariantCulture);
            if (t == typeof(double)) return Convert.ToDouble(v, CultureInfo.InvariantCulture);
            if (t == typeof(bool)) return (bool)v;
            if (t.IsEnum) return Enum.Parse(t, v.ToString(), true);
            if (t.IsArray)
            {
                var src = (List<object>)v;
                var arr = Array.CreateInstance(t.GetElementType(), src.Count);
                for (int k = 0; k < src.Count; k++) arr.SetValue(Map(src[k], t.GetElementType()), k);
                return arr;
            }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                var et = t.GetGenericArguments()[0];
                var list = (IList)Activator.CreateInstance(t);
                foreach (var x in (List<object>)v) list.Add(Map(x, et));
                return list;
            }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var vt = t.GetGenericArguments()[1];
                var dict = (IDictionary)Activator.CreateInstance(t);
                foreach (var kv in (Dictionary<string, object>)v) dict[kv.Key] = Map(kv.Value, vt);
                return dict;
            }
            var obj = Activator.CreateInstance(t);
            var src2 = (Dictionary<string, object>)v;
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object raw;
                if (src2.TryGetValue(f.Name, out raw)) f.SetValue(obj, Map(raw, f.FieldType));
            }
            return obj;
        }
    }
}
