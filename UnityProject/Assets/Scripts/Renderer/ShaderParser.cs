// Ported from: src/renderer/tr_shader.c (parsing portion)
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// ---------------------------------------------------------------------------
// EtShaderStage — one rendering pass inside a Q3/ET shader
// ---------------------------------------------------------------------------
public class EtShaderStage
{
    public string Map;          // texture path ("$lightmap", "$whiteimage", or a real path)
    public string BlendFunc;    // "add", "blend", "filter", or raw GL token pair
    public string AlphaFunc;    // "GT0", "LT128", "GE128"
    public bool   ClampMap;
    public string TcGen;        // "base", "lightmap", "environment"
    public string TcMod;        // raw tcmod string (e.g. "scroll 0.1 0.2")
    public string RgbGen;       // "identity", "vertex", "wave", "entity"
    public string AlphaGen;     // "identity", "vertex", "wave"
    public bool   IsLightmap;   // true when Map == "$lightmap"
}

// ---------------------------------------------------------------------------
// EtShader — a parsed Q3/ET material definition
// ---------------------------------------------------------------------------
public class EtShader
{
    // Surface / content flags (from q_shared.h)
    public const int SURF_SKY      = 0x0100;
    public const int SURF_SLICK    = 0x0002;
    public const int SURF_NOIMPACT = 0x0010;
    public const int SURF_NOMARK   = 0x0020;
    public const int SURF_FLESH    = 0x0200;
    public const int SURF_NODRAW   = 0x0080;
    public const int SURF_NOSTEPS  = 0x2000;

    public const int CONTENTS_WATER = 0x20;
    public const int CONTENTS_LAVA  = 0x08;
    public const int CONTENTS_FOG   = 0x40;

    public string Name;
    public int    SurfaceFlags;
    public int    ContentFlags;
    public bool   Sky;
    public bool   Translucent;
    public bool   NoCull;
    public bool   TwoSided;
    public bool   Glow;
    public bool   Lava;
    public bool   Water;
    public bool   Fog;
    public bool   NoMark;
    public bool   NoImpact;
    public bool   NoSteps;
    public bool   Slick;
    public bool   Mirror;
    public bool   Portal;
    public List<EtShaderStage> Stages = new();

    // ------------------------------------------------------------------
    // ToMaterialRuntime — creates a Unity Material at runtime.
    // Textures are loaded from etmain via RuntimeResourceLoader.
    // ------------------------------------------------------------------
    public Material ToMaterialRuntime()
    {
        Material mat;
        bool isTransparent = Translucent || HasBlendOrAlphaStage();

        if (Sky)
        {
            // ET sky surfaces need a background-queue opaque material.
            // Skybox/6 Sided has no _MainTex so we use Standard/Lit instead
            // and push to the background queue as a visual placeholder.
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")
                            ?? Shader.Find("Standard"));
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Background;
        }
        else if (isTransparent)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")
                            ?? Shader.Find("Standard"));
            // Built-in RP transparency keywords
            mat.SetFloat("_Mode", 3);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        else
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")
                            ?? Shader.Find("Standard"));
        }

        mat.name = Name;
        if (NoCull || TwoSided)
            mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);

        string mainTexPath = FindFirstTextureMap();
        if (!string.IsNullOrEmpty(mainTexPath))
        {
            var tex = RuntimeResourceLoader.LoadTexture(mainTexPath);
            if (tex != null)
            {
                mat.mainTexture = tex;
                // URP uses _BaseMap; set it explicitly to be safe
                if (mat.HasProperty("_BaseMap"))
                    mat.SetTexture("_BaseMap", tex);
            }
            else
            {
                Debug.LogWarning($"[ShaderParser] '{Name}': texture not loaded: '{mainTexPath}'");
            }
        }
        else if (Stages.Count > 0)
        {
            Debug.LogWarning($"[ShaderParser] '{Name}': no texture map found in {Stages.Count} stages " +
                             $"(maps: {string.Join(", ", Stages.ConvertAll(s => s.Map ?? "null"))})");
        }

        return mat;
    }

#if UNITY_EDITOR
    // ------------------------------------------------------------------
    // Convert this shader to a Unity Material.
    // texturesRoot: the Assets-relative folder where texture files live
    //               (e.g. "Assets/Textures").
    // ------------------------------------------------------------------
    public Material ToMaterial(string texturesRoot)
    {
        Material mat;

        if (Sky)
        {
            mat = new Material(Shader.Find("Skybox/6 Sided"));
        }
        else if (Translucent || HasBlendOrAlphaStage())
        {
            mat = new Material(Shader.Find("Standard"));
            mat.SetFloat("_Mode", 3);                               // Transparent
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        else
        {
            mat = new Material(Shader.Find("Standard"));
        }

        mat.name = Name;

        if (NoCull || TwoSided)
            mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);

        // Assign _MainTex from the first non-lightmap stage
        string mainTexPath = FindFirstTextureMap();
        if (!string.IsNullOrEmpty(mainTexPath))
        {
            // Try exact path, then with common extensions
            var tex = TryLoadTexture(texturesRoot, mainTexPath);
            if (tex != null)
                mat.mainTexture = tex;
        }

        return mat;
    }

    private bool HasBlendOrAlphaStage()
    {
        foreach (var stage in Stages)
        {
            if (!string.IsNullOrEmpty(stage.BlendFunc) ||
                !string.IsNullOrEmpty(stage.AlphaFunc))
                return true;
        }
        return false;
    }

    private string FindFirstTextureMap()
    {
        foreach (var stage in Stages)
        {
            if (stage.IsLightmap) continue;
            if (string.IsNullOrEmpty(stage.Map)) continue;
            // Skip ET engine built-in virtual textures — no file in PK3
            if (stage.Map.StartsWith("$") || stage.Map.StartsWith("*")) continue;
            return stage.Map;
        }
        return null;
    }

    private static Texture2D TryLoadTexture(string texturesRoot, string mapPath)
    {
        string[] extensions = { "", ".tga", ".jpg", ".png", ".dds" };
        foreach (var ext in extensions)
        {
            // Try the path verbatim under Assets/
            string assetPath = $"Assets/{mapPath}{ext}";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (tex != null) return tex;

            // Also try under the specified texturesRoot
            string withRoot = $"{texturesRoot}/{mapPath}{ext}";
            tex = AssetDatabase.LoadAssetAtPath<Texture2D>(withRoot);
            if (tex != null) return tex;
        }
        return null;
    }
#endif
}

// ---------------------------------------------------------------------------
// ShaderParser — tokenizer and parser for Q3/ET .shader files
// Corresponds to the parsing phase of tr_shader.c: R_FindShaderByName,
// CreateInternalShader, ParseShader, ParseStage.
// ---------------------------------------------------------------------------
public static class ShaderParser
{
    // ------------------------------------------------------------------
    // ParseFile
    // Parse all shader definitions from the text of a .shader file.
    // Returns a dictionary keyed by shader name (case-insensitive).
    // ------------------------------------------------------------------
    public static Dictionary<string, EtShader> ParseFile(string text)
    {
        var result = new Dictionary<string, EtShader>(StringComparer.OrdinalIgnoreCase);
        var tokens = Tokenize(text);
        int pos = 0;

        while (pos < tokens.Count)
        {
            string name = tokens[pos++];
            if (string.IsNullOrEmpty(name))
                continue;

            // Expect opening brace
            if (pos >= tokens.Count || tokens[pos] != "{")
            {
                // Skip stray tokens
                continue;
            }
            pos++; // consume '{'

            string body = ExtractBlock(tokens, ref pos); // reads until matching '}'
            var shader = ParseShader(name, body);

            if (!result.ContainsKey(shader.Name))
                result[shader.Name] = shader;
        }

        return result;
    }

    // ------------------------------------------------------------------
    // ParseShader
    // Parse a single shader given its name and the raw text of its body
    // (everything between the outermost { }).
    // ------------------------------------------------------------------
    public static EtShader ParseShader(string name, string body)
    {
        var shader = new EtShader { Name = name };
        var tokens = Tokenize(body);
        int pos = 0;

        while (pos < tokens.Count)
        {
            string tok = tokens[pos++];
            if (string.IsNullOrEmpty(tok)) continue;

            switch (tok.ToLowerInvariant())
            {
                // ---- Stage block ----
                case "{":
                {
                    string stageBody = ExtractBlock(tokens, ref pos);
                    var stage = ParseStage(stageBody);
                    shader.Stages.Add(stage);
                    if (stage.IsLightmap)
                        shader.Translucent = false; // lightmap stage is fine
                    if (!string.IsNullOrEmpty(stage.BlendFunc) ||
                        !string.IsNullOrEmpty(stage.AlphaFunc))
                        shader.Translucent = true;
                    break;
                }

                // ---- Global directives ----
                case "surfaceparm":
                {
                    string parm = ConsumeToken(tokens, ref pos);
                    ApplySurfaceParm(shader, parm);
                    break;
                }

                case "cull":
                {
                    string mode = ConsumeToken(tokens, ref pos);
                    if (string.Equals(mode, "none", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(mode, "twosided", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(mode, "disable", StringComparison.OrdinalIgnoreCase))
                    {
                        shader.NoCull   = true;
                        shader.TwoSided = true;
                    }
                    // "back" and "front" are the default; no special flag needed.
                    break;
                }

                case "nopicmip":
                case "nomipmaps":
                case "polygonoffset":
                case "entitymergable":
                case "fogparms":
                case "skyparms":
                case "deformvertexes":
                case "sort":
                case "q3map_sun":
                case "q3map_surfacelight":
                case "q3map_lightimage":
                case "light":
                case "light1":
                    // Consume rest of line — these directives are either flag-only
                    // or have arguments we don't need for Unity rendering.
                    ConsumeRestOfLine(tokens, ref pos, body);
                    break;

                case "portal":
                    shader.Portal = true;
                    break;

                case "mirror":
                    shader.Mirror = true;
                    break;

                // Ignore unknown top-level tokens (could be extended directives)
                default:
                    break;
            }
        }

        return shader;
    }

    // ------------------------------------------------------------------
    // FindShader (Editor only)
    // Search all .shader files under scriptsFolder for a shader by name.
    // scriptsFolder should be an absolute filesystem path.
    // ------------------------------------------------------------------
#if UNITY_EDITOR
    public static EtShader FindShader(string name, string scriptsFolder)
    {
        if (!Directory.Exists(scriptsFolder))
        {
            Debug.LogWarning($"ShaderParser.FindShader: scripts folder not found: {scriptsFolder}");
            return null;
        }

        foreach (var file in Directory.GetFiles(scriptsFolder, "*.shader", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            var shaders = ParseFile(text);
            if (shaders.TryGetValue(name, out var found))
                return found;
        }

        return null;
    }
#endif

    // ------------------------------------------------------------------
    // FindShaderRuntime — searches etmain .shader files via FileSystem.
    // Called at runtime by RuntimeResourceLoader.
    // ------------------------------------------------------------------
    private static readonly Dictionary<string, EtShader> _runtimeCache =
        new Dictionary<string, EtShader>(StringComparer.OrdinalIgnoreCase);
    private static bool _runtimeCacheLoaded;

    public static EtShader FindShaderRuntime(string name)
    {
        if (!_runtimeCacheLoaded)
        {
            _runtimeCacheLoaded = true;
            string[] files = ET.Core.FileSystem.FS_GetFileList("scripts", ".shader");
            Debug.Log($"[ShaderParser] Found {files.Length} .shader files in scripts/: " +
                      (files.Length > 0 ? string.Join(", ", files) : "(none)"));
            foreach (string f in files)
            {
                // FS_GetFileList returns full virtual paths like "scripts/etmain.shader"
                string text = ET.Core.FileSystem.FS_ReadFileText(f);
                if (string.IsNullOrEmpty(text))
                {
                    Debug.LogWarning($"[ShaderParser] Could not read shader file: {f}");
                    continue;
                }
                int before = _runtimeCache.Count;
                foreach (var kv in ParseFile(text))
                    if (!_runtimeCache.ContainsKey(kv.Key))
                        _runtimeCache[kv.Key] = kv.Value;
                Debug.Log($"[ShaderParser] {f}: parsed {_runtimeCache.Count - before} shaders");
            }
            Debug.Log($"[ShaderParser] Loaded {_runtimeCache.Count} ET shader definitions at runtime.");
        }

        if (!_runtimeCache.TryGetValue(name, out var result))
            Debug.Log($"[ShaderParser] No shader def for '{name}', using texture fallback.");
        return result;
    }

    // ------------------------------------------------------------------
    // ParseStage — parse one { ... } stage block
    // ------------------------------------------------------------------
    private static EtShaderStage ParseStage(string body)
    {
        var stage = new EtShaderStage();
        var tokens = Tokenize(body);
        int pos = 0;

        while (pos < tokens.Count)
        {
            string tok = tokens[pos++];
            if (string.IsNullOrEmpty(tok)) continue;

            switch (tok.ToLowerInvariant())
            {
                case "map":
                {
                    string map = ConsumeToken(tokens, ref pos);
                    stage.Map = map;
                    stage.IsLightmap = string.Equals(map, "$lightmap",
                        StringComparison.OrdinalIgnoreCase);
                    break;
                }

                case "clampmap":
                {
                    stage.Map      = ConsumeToken(tokens, ref pos);
                    stage.ClampMap = true;
                    break;
                }

                case "animmap":
                {
                    // animmap <fps> <map1> [map2 ...] — just take the first frame
                    ConsumeToken(tokens, ref pos); // fps
                    stage.Map = ConsumeToken(tokens, ref pos);
                    // Skip any additional frames on this logical line
                    while (pos < tokens.Count && tokens[pos] != "}" &&
                           !IsKeyword(tokens[pos]))
                        pos++;
                    break;
                }

                case "blendfunc":
                {
                    string src = ConsumeToken(tokens, ref pos);
                    // Could be a shorthand ("blend", "add", "filter") or a GL pair
                    string srcLower = src.ToLowerInvariant();
                    if (srcLower == "blend" || srcLower == "add" || srcLower == "filter")
                    {
                        stage.BlendFunc = srcLower;
                    }
                    else
                    {
                        // GL_xxx GL_xxx
                        string dst = ConsumeToken(tokens, ref pos);
                        stage.BlendFunc = $"{src} {dst}";
                    }
                    break;
                }

                case "alphafunc":
                    stage.AlphaFunc = ConsumeToken(tokens, ref pos).ToUpperInvariant();
                    break;

                case "tcgen":
                    stage.TcGen = ConsumeToken(tokens, ref pos).ToLowerInvariant();
                    break;

                case "tcmod":
                {
                    // tcmod <type> [params...] — capture the full sub-line
                    string modType = ConsumeToken(tokens, ref pos);
                    var sb = new System.Text.StringBuilder(modType);
                    // Collect parameters until we hit a recognised keyword or block end
                    while (pos < tokens.Count && tokens[pos] != "}" &&
                           !IsKeyword(tokens[pos]))
                    {
                        sb.Append(' ');
                        sb.Append(tokens[pos++]);
                    }
                    stage.TcMod = sb.ToString();
                    break;
                }

                case "rgbgen":
                {
                    string gen = ConsumeToken(tokens, ref pos);
                    stage.RgbGen = gen.ToLowerInvariant();
                    // Some rgbGen types have parameters (wave, etc.) — skip them
                    if (stage.RgbGen == "wave")
                    {
                        for (int k = 0; k < 5 && pos < tokens.Count &&
                             tokens[pos] != "}"; k++)
                            pos++;
                    }
                    break;
                }

                case "alphagen":
                {
                    string gen = ConsumeToken(tokens, ref pos);
                    stage.AlphaGen = gen.ToLowerInvariant();
                    if (stage.AlphaGen == "wave")
                    {
                        for (int k = 0; k < 5 && pos < tokens.Count &&
                             tokens[pos] != "}"; k++)
                            pos++;
                    }
                    break;
                }

                case "detail":
                case "depthwrite":
                case "depthfunc":
                    // Flag-only or single-arg directives we don't need to track
                    if (tok == "depthfunc")
                        ConsumeToken(tokens, ref pos);
                    break;

                default:
                    break;
            }
        }

        return stage;
    }

    // ------------------------------------------------------------------
    // ApplySurfaceParm — map a surfaceparm keyword to flags on the shader
    // Source: q_shared.h SURF_* and CONTENTS_* definitions
    // ------------------------------------------------------------------
    private static void ApplySurfaceParm(EtShader shader, string parm)
    {
        if (string.IsNullOrEmpty(parm)) return;
        switch (parm.ToLowerInvariant())
        {
            // Surface flags
            case "sky":
                shader.Sky          = true;
                shader.SurfaceFlags |= EtShader.SURF_SKY;
                break;
            case "slick":
                shader.Slick        = true;
                shader.SurfaceFlags |= EtShader.SURF_SLICK;
                break;
            case "noimpact":
                shader.NoImpact     = true;
                shader.SurfaceFlags |= EtShader.SURF_NOIMPACT;
                break;
            case "nomark":
                shader.NoMark       = true;
                shader.SurfaceFlags |= EtShader.SURF_NOMARK;
                break;
            case "nodraw":
                shader.SurfaceFlags |= EtShader.SURF_NODRAW;
                break;
            case "nosteps":
                shader.NoSteps      = true;
                shader.SurfaceFlags |= EtShader.SURF_NOSTEPS;
                break;
            case "flesh":
                shader.SurfaceFlags |= EtShader.SURF_FLESH;
                break;

            // Content flags
            case "water":
                shader.Water        = true;
                shader.ContentFlags |= EtShader.CONTENTS_WATER;
                break;
            case "lava":
                shader.Lava         = true;
                shader.ContentFlags |= EtShader.CONTENTS_LAVA;
                break;
            case "fog":
                shader.Fog          = true;
                shader.ContentFlags |= EtShader.CONTENTS_FOG;
                break;

            // Boolean flags with no numeric mapping needed
            case "glow":
                shader.Glow    = true;
                break;
            case "nonsolid":
            case "nolightmap":
            case "pointlight":
            case "metalsteps":
            case "dust":
            case "clusterportal":
            case "donotenter":
            case "origin":
            case "trans":
            case "areaportal":
            case "playerclip":
            case "monsterclip":
            case "structural":
            case "detail":
            case "ladder":
                // Acknowledged but not mapped to a Unity-side flag.
                break;

            default:
                // Unknown surfaceparm — ignore gracefully.
                break;
        }
    }

    // ------------------------------------------------------------------
    // Tokenizer
    // Splits shader source text into tokens, treating { and } as
    // standalone tokens, stripping // comments and blank lines.
    // ------------------------------------------------------------------
    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>(256);
        int i = 0;
        int len = text?.Length ?? 0;

        while (i < len)
        {
            char c = text[i];

            // Skip whitespace
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
            {
                i++;
                continue;
            }

            // Line comment  //
            if (c == '/' && i + 1 < len && text[i + 1] == '/')
            {
                while (i < len && text[i] != '\n') i++;
                continue;
            }

            // Block comment  /* ... */
            if (c == '/' && i + 1 < len && text[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < len && !(text[i] == '*' && text[i + 1] == '/'))
                    i++;
                i += 2;
                continue;
            }

            // Braces are standalone tokens
            if (c == '{' || c == '}')
            {
                tokens.Add(c.ToString());
                i++;
                continue;
            }

            // Quoted string
            if (c == '"')
            {
                i++;
                int start = i;
                while (i < len && text[i] != '"') i++;
                tokens.Add(text.Substring(start, i - start));
                if (i < len) i++; // skip closing quote
                continue;
            }

            // Regular token — read until whitespace or brace
            {
                int start = i;
                while (i < len && text[i] != ' ' && text[i] != '\t' &&
                       text[i] != '\r' && text[i] != '\n' &&
                       text[i] != '{' && text[i] != '}')
                    i++;
                tokens.Add(text.Substring(start, i - start));
            }
        }

        return tokens;
    }

    // ------------------------------------------------------------------
    // ExtractBlock
    // pos points to the first token AFTER the opening '{'.
    // Reads tokens until the matching '}', handling nested braces.
    // Leaves pos pointing to the token AFTER the closing '}'.
    // Returns the contents between the braces as a single string.
    // ------------------------------------------------------------------
    private static string ExtractBlock(List<string> tokens, ref int pos)
    {
        var sb = new System.Text.StringBuilder();
        int depth = 1;

        while (pos < tokens.Count)
        {
            string tok = tokens[pos++];
            if (tok == "{")
            {
                depth++;
                sb.Append("{ ");
            }
            else if (tok == "}")
            {
                depth--;
                if (depth == 0)
                    break;
                sb.Append("} ");
            }
            else
            {
                sb.Append(tok);
                sb.Append(' ');
            }
        }

        return sb.ToString();
    }

    // ------------------------------------------------------------------
    private static string ConsumeToken(List<string> tokens, ref int pos)
    {
        if (pos < tokens.Count)
            return tokens[pos++];
        return string.Empty;
    }

    // ------------------------------------------------------------------
    // ConsumeRestOfLine
    // Advance pos past tokens that belong to the current logical line.
    // Since we have already tokenized, we approximate by consuming
    // tokens that don't look like top-level shader keywords.
    // ------------------------------------------------------------------
    private static void ConsumeRestOfLine(List<string> tokens, ref int pos, string body)
    {
        // Consume until we hit a brace or a recognized top-level keyword
        while (pos < tokens.Count &&
               tokens[pos] != "{" &&
               tokens[pos] != "}" &&
               !IsKeyword(tokens[pos]))
        {
            pos++;
        }
    }

    // Set of top-level shader directive keywords — used to detect line boundaries
    // in the absence of newline information after tokenization.
    private static readonly HashSet<string> s_keywords = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        "surfaceparm", "cull", "nopicmip", "nomipmaps", "polygonoffset",
        "sort", "portal", "mirror", "fogparms", "skyparms", "deformvertexes",
        "q3map_sun", "q3map_surfacelight", "q3map_lightimage",
        "map", "clampmap", "animmap", "blendfunc", "alphafunc",
        "tcgen", "tcmod", "rgbgen", "alphagen", "detail",
        "depthwrite", "depthfunc", "entitymergable", "light", "light1",
    };

    private static bool IsKeyword(string tok) => s_keywords.Contains(tok);
}
