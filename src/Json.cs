using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BifrostUpdater
{
    /// <summary>
    /// Tiny JSON reader for the GitHub API answers. Objects become Dictionary&lt;string, object&gt;,
    /// arrays List&lt;object&gt;, numbers double, plus string, bool and null.
    /// The patcher runs before any other mod is loaded, so there is no JSON library to rely on.
    /// </summary>
    internal static class Json
    {
        public static object Parse(string text)
        {
            int i = 0;
            object value = ReadValue(text, ref i);
            SkipSpace(text, ref i);
            if (i != text.Length)
            {
                throw new FormatException("Unexpected data after JSON value.");
            }

            return value;
        }

        public static Dictionary<string, object> Obj(object value, string key)
        {
            return Get(value, key) as Dictionary<string, object>;
        }

        public static string Str(object value, string key)
        {
            return Get(value, key) as string;
        }

        public static List<object> List(object value, string key)
        {
            return Get(value, key) as List<object>;
        }

        public static double Num(object value, string key)
        {
            object n = Get(value, key);
            return n is double d ? d : 0;
        }

        public static object Get(object value, string key)
        {
            var map = value as Dictionary<string, object>;
            return map != null && map.TryGetValue(key, out object result) ? result : null;
        }

        private static void SkipSpace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
            {
                i++;
            }
        }

        private static object ReadValue(string s, ref int i)
        {
            SkipSpace(s, ref i);
            if (i >= s.Length)
            {
                throw new FormatException("Unexpected end of JSON.");
            }

            char c = s[i];
            if (c == '{')
            {
                return ReadObject(s, ref i);
            }

            if (c == '[')
            {
                return ReadArray(s, ref i);
            }

            if (c == '"')
            {
                return ReadString(s, ref i);
            }

            if (Match(s, ref i, "true"))
            {
                return true;
            }

            if (Match(s, ref i, "false"))
            {
                return false;
            }

            if (Match(s, ref i, "null"))
            {
                return null;
            }

            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0)
            {
                i++;
            }

            if (i == start || !double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            {
                throw new FormatException("Invalid JSON at position " + start + ".");
            }

            return number;
        }

        private static bool Match(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
            {
                return false;
            }

            i += word.Length;
            return true;
        }

        private static Dictionary<string, object> ReadObject(string s, ref int i)
        {
            var map = new Dictionary<string, object>();
            i++; // {
            SkipSpace(s, ref i);
            if (i < s.Length && s[i] == '}')
            {
                i++;
                return map;
            }

            while (true)
            {
                SkipSpace(s, ref i);
                if (i >= s.Length || s[i] != '"')
                {
                    throw new FormatException("Expected a property name at position " + i + ".");
                }

                string key = ReadString(s, ref i);
                SkipSpace(s, ref i);
                if (i >= s.Length || s[i] != ':')
                {
                    throw new FormatException("Expected ':' at position " + i + ".");
                }

                i++;
                map[key] = ReadValue(s, ref i);
                SkipSpace(s, ref i);
                if (i < s.Length && s[i] == ',')
                {
                    i++;
                    continue;
                }

                if (i < s.Length && s[i] == '}')
                {
                    i++;
                    return map;
                }

                throw new FormatException("Expected ',' or '}' at position " + i + ".");
            }
        }

        private static List<object> ReadArray(string s, ref int i)
        {
            var list = new List<object>();
            i++; // [
            SkipSpace(s, ref i);
            if (i < s.Length && s[i] == ']')
            {
                i++;
                return list;
            }

            while (true)
            {
                list.Add(ReadValue(s, ref i));
                SkipSpace(s, ref i);
                if (i < s.Length && s[i] == ',')
                {
                    i++;
                    continue;
                }

                if (i < s.Length && s[i] == ']')
                {
                    i++;
                    return list;
                }

                throw new FormatException("Expected ',' or ']' at position " + i + ".");
            }
        }

        private static string ReadString(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++; // opening quote
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"')
                {
                    return sb.ToString();
                }

                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }

                if (i >= s.Length)
                {
                    break;
                }

                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 > s.Length)
                        {
                            throw new FormatException("Invalid \\u escape.");
                        }

                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }

            throw new FormatException("Unterminated JSON string.");
        }
    }
}
