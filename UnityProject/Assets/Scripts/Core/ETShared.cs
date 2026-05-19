// Ported from: src/game/q_shared.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using System;
using System.Text;
using UnityEngine;

namespace ET.Core
{
    public static class ETShared
    {
        // -------------------------------------------------------
        // Constants  (mirrors q_shared.h defines)
        // -------------------------------------------------------
        public const int MAX_QPATH        = 64;
        public const int MAX_TOKEN_CHARS  = 1024;
        public const int MAX_INFO_STRING  = 1024;
        public const int MAX_INFO_KEY     = 1024;
        public const int MAX_INFO_VALUE   = 1024;
        public const int BIG_INFO_STRING  = 8192;
        public const int BIG_INFO_KEY     = 8192;
        public const int BIG_INFO_VALUE   = 8192;

        // -------------------------------------------------------
        // General utilities
        // -------------------------------------------------------

        public static float Com_Clamp(float min, float max, float value)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        // Normalise path separators to forward-slash (COM_FixPath)
        public static string COM_FixPath(string path)
            => path.Replace('\\', '/');

        // Returns everything after the last '/' (COM_SkipPath)
        public static string COM_SkipPath(string path)
        {
            int idx = path.LastIndexOf('/');
            return idx >= 0 ? path[(idx + 1)..] : path;
        }

        // Strips file extension (COM_StripExtension)
        public static string COM_StripExtension(string path)
        {
            int dot = path.LastIndexOf('.');
            int slash = path.LastIndexOf('/');
            return (dot > slash) ? path[..dot] : path;
        }

        // Strips filename, keeping directory (COM_StripFilename)
        public static string COM_StripFilename(string path)
        {
            string skipped = COM_SkipPath(path);
            return skipped.Length < path.Length
                ? path[..(path.Length - skipped.Length)]
                : string.Empty;
        }

        // Appends extension if path has none (COM_DefaultExtension)
        public static string COM_DefaultExtension(string path, string extension)
        {
            string filename = COM_SkipPath(path);
            if (!filename.Contains('.'))
                return path + extension;
            return path;
        }

        // -------------------------------------------------------
        // Bit array operations (> 32-bit arrays, mirrors COM_BitCheck/Set/Clear)
        // -------------------------------------------------------

        public static bool COM_BitCheck(int[] array, int bitNum)
        {
            int i = 0;
            while (bitNum > 31) { i++; bitNum -= 32; }
            return (array[i] & (1 << bitNum)) != 0;
        }

        public static void COM_BitSet(int[] array, int bitNum)
        {
            int i = 0;
            while (bitNum > 31) { i++; bitNum -= 32; }
            array[i] |= (1 << bitNum);
        }

        public static void COM_BitClear(int[] array, int bitNum)
        {
            int i = 0;
            while (bitNum > 31) { i++; bitNum -= 32; }
            array[i] &= ~(1 << bitNum);
        }

        // -------------------------------------------------------
        // Byte-order helpers
        // C# on x86/ARM is always little-endian at runtime.
        // These match the LittleShort/LittleLong/BigShort/BigLong
        // runtime dispatch that ET's Swap_Init() set up.
        // -------------------------------------------------------

        public static short  LittleShort(short v)  => v;
        public static int    LittleLong (int   v)  => v;
        public static float  LittleFloat(float v)  => v;

        public static short BigShort(short v)
            => (short)(((v & 0x00FF) << 8) | ((v >> 8) & 0x00FF));

        public static int BigLong(int v)
            => ((v & 0x000000FF) << 24) | ((v & 0x0000FF00) <<  8) |
               ((v & 0x00FF0000) >>  8) | ((int)((uint)v & 0xFF000000) >> 24);

        public static float BigFloat(float v)
        {
            byte[] b = BitConverter.GetBytes(v);
            Array.Reverse(b);
            return BitConverter.ToSingle(b, 0);
        }

        // -------------------------------------------------------
        // String utilities  (Q_str* family)
        // -------------------------------------------------------

        public static bool Q_isprint(int c)  => c >= 0x20 && c <= 0x7E;
        public static bool Q_islower(int c)  => c >= 'a'  && c <= 'z';
        public static bool Q_isupper(int c)  => c >= 'A'  && c <= 'Z';
        public static bool Q_isalpha(int c)  => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        public static bool Q_isnumeric(int c)=> c >= '0'  && c <= '9';
        public static bool Q_isalphanumeric(int c) => Q_isalpha(c) || Q_isnumeric(c);
        public static bool Q_isforfilename(int c)  => Q_isalphanumeric(c) || c == '_';

        public static int Q_stricmp(string s1, string s2)
            => string.Compare(s1, s2, StringComparison.OrdinalIgnoreCase);

        public static int Q_stricmpn(string s1, string s2, int n)
            => string.Compare(s1, 0, s2, 0, n, StringComparison.OrdinalIgnoreCase);

        public static int Q_strncmp(string s1, string s2, int n)
            => string.Compare(s1, 0, s2, 0, n, StringComparison.Ordinal);

        public static string Q_strlwr(string s) => s.ToLowerInvariant();
        public static string Q_strupr(string s) => s.ToUpperInvariant();

        // Strips ^N color codes, returns printable-only string (Q_CleanStr)
        public static string Q_CleanStr(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (Q_IsColorString(s, i))
                {
                    i++; // skip color char
                }
                else if (s[i] >= 0x20 && s[i] <= 0x7E)
                {
                    sb.Append(s[i]);
                }
            }
            return sb.ToString();
        }

        // Length excluding ^N color codes (Q_PrintStrlen)
        public static int Q_PrintStrlen(string s)
        {
            int len = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (Q_IsColorString(s, i)) { i++; continue; }
                len++;
            }
            return len;
        }

        // True if s[i] is '^' followed by a colour index (Q_IsColorString macro)
        public static bool Q_IsColorString(string s, int i)
            => i + 1 < s.Length && s[i] == '^' && s[i + 1] != '^';

        public static bool Q_isBadDirChar(char c)
        {
            const string bad = ";& ()|<>*?[]~+@!\\/'\"\0";
            return bad.IndexOf(c) >= 0;
        }

        public static string Q_CleanDirName(string dirname)
        {
            var sb = new StringBuilder();
            int start = 0;
            while (start < dirname.Length && dirname[start] == '.') start++;
            for (int i = start; i < dirname.Length; i++)
            {
                if (!Q_isBadDirChar(dirname[i])) sb.Append(dirname[i]);
            }
            return sb.ToString();
        }

        // -------------------------------------------------------
        // Token / text parsing   (COM_Parse family)
        // -------------------------------------------------------

        public class ParseSession
        {
            public string Name  { get; private set; }
            public int    Lines { get; set; }

            private int     _pos;
            private string  _data;
            private int     _backupPos;
            private int     _backupLines;

            public ParseSession(string name, string data)
            {
                Name  = name;
                Lines = 0;
                _data = data ?? string.Empty;
                _pos  = 0;
            }

            public void Backup()
            {
                _backupPos   = _pos;
                _backupLines = Lines;
            }

            public void Restore()
            {
                _pos  = _backupPos;
                Lines = _backupLines;
            }

            public bool AtEnd => _pos >= _data.Length;

            // Returns next token or empty string. allowLineBreaks=false
            // stops at newlines and returns empty.
            public string ParseExt(bool allowLineBreaks)
            {
                Backup();

                // skip whitespace
                bool hasNewLines = false;
                while (_pos < _data.Length)
                {
                    char c = _data[_pos];
                    if (c > ' ') break;
                    if (c == '\n') { Lines++; hasNewLines = true; }
                    _pos++;
                }

                if (_pos >= _data.Length) return string.Empty;
                if (hasNewLines && !allowLineBreaks) return string.Empty;

                // skip line comments and block comments
                while (_pos < _data.Length)
                {
                    if (_pos + 1 < _data.Length && _data[_pos] == '/' && _data[_pos + 1] == '/')
                    {
                        while (_pos < _data.Length && _data[_pos] != '\n') _pos++;
                        SkipWhitespace(allowLineBreaks, ref hasNewLines);
                        if (!allowLineBreaks && hasNewLines) return string.Empty;
                        continue;
                    }
                    if (_pos + 1 < _data.Length && _data[_pos] == '/' && _data[_pos + 1] == '*')
                    {
                        _pos += 2;
                        while (_pos + 1 < _data.Length && !(_data[_pos] == '*' && _data[_pos+1] == '/'))
                            _pos++;
                        if (_pos + 1 < _data.Length) _pos += 2;
                        SkipWhitespace(allowLineBreaks, ref hasNewLines);
                        if (!allowLineBreaks && hasNewLines) return string.Empty;
                        continue;
                    }
                    break;
                }

                if (_pos >= _data.Length) return string.Empty;

                // quoted string
                if (_data[_pos] == '"')
                {
                    _pos++;
                    var sb = new StringBuilder();
                    while (_pos < _data.Length)
                    {
                        char c = _data[_pos++];
                        if (c == '"') break;
                        if (sb.Length < MAX_TOKEN_CHARS) sb.Append(c);
                    }
                    return sb.ToString();
                }

                // regular token
                {
                    var sb = new StringBuilder();
                    while (_pos < _data.Length && _data[_pos] > ' ')
                    {
                        if (_data[_pos] == '\n') Lines++;
                        if (sb.Length < MAX_TOKEN_CHARS) sb.Append(_data[_pos]);
                        _pos++;
                    }
                    return sb.Length == MAX_TOKEN_CHARS ? string.Empty : sb.ToString();
                }
            }

            public string Parse() => ParseExt(true);

            public void SkipBracedSection()
            {
                int depth = 0;
                do
                {
                    string token = ParseExt(true);
                    if (token == "{") depth++;
                    else if (token == "}") depth--;
                } while (depth > 0 && !AtEnd);
            }

            public void SkipRestOfLine()
            {
                while (_pos < _data.Length && _data[_pos] != '\n') _pos++;
                if (_pos < _data.Length) { Lines++; _pos++; }
            }

            private void SkipWhitespace(bool allowLineBreaks, ref bool hasNewLines)
            {
                while (_pos < _data.Length)
                {
                    char c = _data[_pos];
                    if (c > ' ') break;
                    if (c == '\n') { Lines++; hasNewLines = true; }
                    _pos++;
                }
            }
        }

        // COM_Compress: strip comments, collapse whitespace (in-place on char[])
        public static string COM_Compress(string data)
        {
            var sb = new StringBuilder(data.Length);
            int i = 0;
            bool ws = false;
            while (i < data.Length)
            {
                char c = data[i];
                if (c == '\r' || c == '\n')
                {
                    sb.Append(c); ws = false; i++; continue;
                }
                if (c == '/' && i + 1 < data.Length && data[i + 1] == '/')
                {
                    while (i < data.Length && data[i] != '\n') i++;
                    ws = false; continue;
                }
                if (c == '/' && i + 1 < data.Length && data[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < data.Length && !(data[i] == '*' && data[i+1] == '/')) i++;
                    if (i + 1 < data.Length) i += 2;
                    ws = false; continue;
                }
                if (ws) { sb.Append(' '); }
                sb.Append(c); ws = false; i++;
            }
            return sb.ToString();
        }

        // -------------------------------------------------------
        // Info strings  (\key\value\key\value...)
        // -------------------------------------------------------

        public static string Info_ValueForKey(string s, string key)
        {
            if (string.IsNullOrEmpty(s) || string.IsNullOrEmpty(key)) return string.Empty;
            if (s.Length >= BIG_INFO_STRING)
                throw new Exception($"Info_ValueForKey: oversize infostring");

            ReadOnlySpan<char> span = s.AsSpan();
            int pos = 0;
            if (pos < span.Length && span[pos] == '\\') pos++;

            while (pos < span.Length)
            {
                // read key
                int keyStart = pos;
                while (pos < span.Length && span[pos] != '\\') pos++;
                string pkey = span[keyStart..pos].ToString();
                if (pos < span.Length) pos++; // skip '\\'

                // read value
                int valStart = pos;
                while (pos < span.Length && span[pos] != '\\') pos++;
                string val = span[valStart..pos].ToString();
                if (pos < span.Length) pos++;

                if (Q_stricmp(key, pkey) == 0) return val;
            }
            return string.Empty;
        }

        public static void Info_SetValueForKey(ref string s, string key, string value)
        {
            if (s.Length >= MAX_INFO_STRING)
                throw new Exception("Info_SetValueForKey: oversize infostring");

            Info_RemoveKey(ref s, key);
            if (string.IsNullOrEmpty(value)) return;

            string newi = $"\\{key}\\{value}";
            if (newi.Length + s.Length > MAX_INFO_STRING)
            {
                Debug.LogWarning("Info string length exceeded");
                return;
            }
            s += newi;
        }

        public static void Info_SetValueForKey_Big(ref string s, string key, string value)
        {
            if (s.Length >= BIG_INFO_STRING)
                throw new Exception("Info_SetValueForKey_Big: oversize infostring");

            Info_RemoveKey_Big(ref s, key);
            if (string.IsNullOrEmpty(value)) return;

            string newi = $"\\{key}\\{value}";
            if (newi.Length + s.Length > BIG_INFO_STRING)
            {
                Debug.LogWarning("BIG Info string length exceeded");
                return;
            }
            s += newi;
        }

        public static void Info_RemoveKey(ref string s, string key)
            => s = RemoveKey(s, key, MAX_INFO_STRING);

        public static void Info_RemoveKey_Big(ref string s, string key)
            => s = RemoveKey(s, key, BIG_INFO_STRING);

        private static string RemoveKey(string s, string key, int limit)
        {
            if (s.Length >= limit) throw new Exception("Info_RemoveKey: oversize infostring");
            if (key.Contains('\\')) return s;

            var sb   = new StringBuilder(s);
            int pos  = 0;
            if (pos < sb.Length && sb[pos] == '\\') pos++;

            while (pos < sb.Length)
            {
                int start = pos - (pos > 0 && sb[pos-1] == '\\' ? 1 : 0);
                int keyStart = pos;
                while (pos < sb.Length && sb[pos] != '\\') pos++;
                string pkey = sb.ToString(keyStart, pos - keyStart);
                if (pos < sb.Length) pos++;

                int valStart = pos;
                while (pos < sb.Length && sb[pos] != '\\') pos++;
                if (pos < sb.Length) pos++;

                if (Q_stricmp(key, pkey) == 0)
                {
                    // rebuild without this pair
                    string before = s[..start];
                    string after  = pos < s.Length ? s[pos..] : string.Empty;
                    return before + (after.Length > 0 && !after.StartsWith("\\") && before.Length > 0 ? after : after);
                }
            }
            return s;
        }

        public static bool Info_Validate(string s)
            => !s.Contains('"') && !s.Contains(';');

        public static int Com_ParseInfos(string buf, int max, string[][] infos)
        {
            var session = new ParseSession("parseinfos", buf);
            int count = 0;
            while (count < max)
            {
                string token = session.Parse();
                if (string.IsNullOrEmpty(token)) break;
                if (token != "{") { Debug.LogWarning("Missing { in info file"); break; }

                var kv = new System.Collections.Generic.Dictionary<string, string>();
                while (true)
                {
                    token = session.Parse();
                    if (string.IsNullOrEmpty(token)) { Debug.LogWarning("Unexpected end of info file"); break; }
                    if (token == "}") break;
                    string k = token;
                    string v = session.ParseExt(false);
                    if (string.IsNullOrEmpty(v)) v = "<NULL>";
                    kv[k] = v;
                }
                // Pack into flat \key\value string
                string s = string.Empty;
                foreach (var pair in kv)
                    Info_SetValueForKey(ref s, pair.Key, pair.Value);

                infos[count] = new[] { s };
                count++;
            }
            return count;
        }
    }
}
