// Ported from: src/qcommon/files.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace ET.Core
{
    /// <summary>
    /// Virtual filesystem with PK3 (ZIP) support (files.c port).
    /// Search path: directories listed in descending priority, each containing
    /// loose files and/or PK3 archives. Matches files.c's layered search logic.
    /// </summary>
    public static class FileSystem
    {
        private static readonly List<SearchPath> _searchPaths = new List<SearchPath>();

        // FS_Startup — add a directory (and its PK3s) to the search path
        public static void FS_AddGameDirectory(string dir)
        {
            if (!Directory.Exists(dir))
            {
                Debug.LogWarning($"[FileSystem] Directory not found: {dir}");
                return;
            }

            // Loose files in this dir (highest priority within the dir)
            _searchPaths.Add(new LooseDirectory(dir));

            // PK3s in this dir, sorted by name (later names = higher priority in ET)
            var pk3s = Directory.GetFiles(dir, "*.pk3");
            Array.Sort(pk3s, StringComparer.OrdinalIgnoreCase);
            foreach (var pk3 in pk3s)
                _searchPaths.Add(new Pk3Archive(pk3));

            Debug.Log($"[FileSystem] Added game directory: {dir} ({pk3s.Length} PK3s)");
        }

        // FS_ReadFile — load a virtual file into a byte array; returns null if not found
        public static byte[] FS_ReadFile(string path)
        {
            // Normalize path separator
            path = path.Replace('\\', '/');

            // Search in reverse so last-added (highest priority) is checked first
            for (int i = _searchPaths.Count - 1; i >= 0; i--)
            {
                var data = _searchPaths[i].ReadFile(path);
                if (data != null) return data;
            }
            return null;
        }

        // FS_ReadFileText — convenience overload
        public static string FS_ReadFileText(string path)
        {
            var data = FS_ReadFile(path);
            return data == null ? null : System.Text.Encoding.UTF8.GetString(data);
        }

        // FS_FileExists
        public static bool FS_FileExists(string path)
        {
            path = path.Replace('\\', '/');
            for (int i = _searchPaths.Count - 1; i >= 0; i--)
                if (_searchPaths[i].FileExists(path)) return true;
            return false;
        }

        // FS_GetFileList — list files in a virtual directory matching an extension
        public static string[] FS_GetFileList(string dir, string ext)
        {
            dir = dir.Replace('\\', '/').TrimEnd('/') + "/";
            ext = ext.ToLowerInvariant();

            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = _searchPaths.Count - 1; i >= 0; i--)
                foreach (var f in _searchPaths[i].ListFiles(dir, ext))
                    results.Add(f);

            var arr = new string[results.Count];
            results.CopyTo(arr);
            Array.Sort(arr, StringComparer.OrdinalIgnoreCase);
            return arr;
        }

        // FS_WriteFile — write a file to the first writable search path (or Application.persistentDataPath)
        public static void FS_WriteFile(string path, byte[] data)
        {
            path = path.Replace('\\', '/');
            string fullPath = Path.Combine(Application.persistentDataPath, path);
            string dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllBytes(fullPath, data);
        }

        // FS_Shutdown — clear all search paths
        public static void FS_Shutdown()
        {
            foreach (var sp in _searchPaths)
                (sp as IDisposable)?.Dispose();
            _searchPaths.Clear();
        }

        // -----------------------------------------------------------------------
        // Internal search path abstractions
        // -----------------------------------------------------------------------

        private abstract class SearchPath
        {
            public abstract byte[]     ReadFile(string path);
            public abstract bool       FileExists(string path);
            public abstract IEnumerable<string> ListFiles(string dir, string ext);
        }

        private class LooseDirectory : SearchPath
        {
            private readonly string _root;
            public LooseDirectory(string root) { _root = root; }

            public override byte[] ReadFile(string path)
            {
                string full = Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar));
                return File.Exists(full) ? File.ReadAllBytes(full) : null;
            }

            public override bool FileExists(string path)
            {
                string full = Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar));
                return File.Exists(full);
            }

            public override IEnumerable<string> ListFiles(string dir, string ext)
            {
                string fullDir = Path.Combine(_root, dir.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(fullDir)) yield break;
                foreach (var f in Directory.GetFiles(fullDir))
                {
                    if (ext != "*" && !f.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                        continue;
                    yield return dir + Path.GetFileName(f);
                }
            }
        }

        private class Pk3Archive : SearchPath, IDisposable
        {
            private readonly string       _path;
            private ZipArchive            _zip;
            private bool                  _disposed;

            public Pk3Archive(string path)
            {
                _path = path;
                try { _zip = ZipFile.OpenRead(path); }
                catch (Exception e)
                {
                    Debug.LogWarning($"[FileSystem] Failed to open PK3 {path}: {e.Message}");
                }
            }

            private ZipArchive Zip => _zip;

            public override byte[] ReadFile(string path)
            {
                if (Zip == null) return null;
                var entry = Zip.GetEntry(path) ?? Zip.GetEntry(path.ToLowerInvariant());
                if (entry == null) return null;
                using var stream = entry.Open();
                var data = new byte[entry.Length];
                int read = 0;
                while (read < data.Length)
                {
                    int n = stream.Read(data, read, data.Length - read);
                    if (n == 0) break;
                    read += n;
                }
                return data;
            }

            public override bool FileExists(string path)
            {
                if (Zip == null) return false;
                return Zip.GetEntry(path) != null || Zip.GetEntry(path.ToLowerInvariant()) != null;
            }

            public override IEnumerable<string> ListFiles(string dir, string ext)
            {
                if (Zip == null) yield break;
                foreach (var entry in Zip.Entries)
                {
                    string name = entry.FullName.Replace('\\', '/');
                    if (!name.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) continue;
                    if (ext != "*" && !name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) continue;
                    yield return name;
                }
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _zip?.Dispose();
                _zip = null;
            }
        }
    }
}
