// Ports: cg_sound.c — cgame-side sound script system
// The Speaker Editor (debug tool using OpenGL glBegin/glEnd) is omitted;
// all rendering events are published via OnSpeakerDraw for Unity subscribers.
// Core sound script lookup + playback delegates to AudioSystem (ET.Game).

using System;
using System.Collections.Generic;
using UnityEngine;
using ET.Core;
using ET.Game;

namespace ET.Client
{
    // -------------------------------------------------------------------------
    // Sound entry within a sound script sound group
    // -------------------------------------------------------------------------
    public class SoundScriptFile
    {
        public string Filename  = "";
        public AudioClip Clip;          // loaded at precache time
        public bool Compressed;
    }

    // -------------------------------------------------------------------------
    // One sound group (soundScriptSound_t) — multiple filenames, pick randomly
    // -------------------------------------------------------------------------
    public class SoundScriptSound
    {
        public List<SoundScriptFile> Sounds = new List<SoundScriptFile>();
        public int  LastPlayed;         // cgame time when last played
        public SoundScriptSound Next;
    }

    // -------------------------------------------------------------------------
    // A named sound script (soundScript_t)
    // -------------------------------------------------------------------------
    public class SoundScript
    {
        public string Name      = "";
        public bool   Looping;
        public bool   Streaming;
        public bool   Random;
        public float  Volume    = 1f;
        public float  MinDist   = 0f;
        public SoundScriptSound SoundList;
        public int    Index;            // 1-based index into _scripts list
        public SoundScript NextHash;    // hash-chain link
    }

    // -------------------------------------------------------------------------
    // Buffered (sequential) sound queue entry
    // -------------------------------------------------------------------------
    public class BufferedSoundScript
    {
        public SoundScript Script;
        public int EndTime;
    }

    // -------------------------------------------------------------------------
    // Script speaker (world-placed ambient sound trigger)
    // -------------------------------------------------------------------------
    public class ScriptSpeaker
    {
        public Vector3 Origin;
        public string  SoundScript = "";
        public float   MinDist;
        public float   MaxDist;
        public bool    Looping;
        public bool    Activated;
        public string  TargetName  = "";
        public int     Wait;
        public int     Random;
        public int     Index;
    }

    // -------------------------------------------------------------------------
    // CGameSound — main cgame sound script manager
    // MAX_SOUND_SCRIPTS = 4096, MAX_SOUND_SCRIPT_SOUNDS = 8192
    // -------------------------------------------------------------------------
    public class CGameSound
    {
        private const int FILE_HASH_SIZE             = 1024;
        private const int MAX_SOUND_SCRIPTS          = 4096;
        private const int MAX_SOUND_SCRIPT_SOUNDS    = 8192;
        private const int MAX_BUFFERED_SOUNDSCRIPTS  = 16;
        private const int MAX_SPEAKERS               = 256;

        private readonly SoundScript[]      _scripts      = new SoundScript[MAX_SOUND_SCRIPTS];
        private readonly SoundScriptSound[] _sounds       = new SoundScriptSound[MAX_SOUND_SCRIPT_SOUNDS];
        private readonly SoundScript[]      _hashTable    = new SoundScript[FILE_HASH_SIZE];
        private int _numScripts, _numSounds;

        private readonly BufferedSoundScript[] _buffer =
            new BufferedSoundScript[MAX_BUFFERED_SOUNDSCRIPTS];
        private int _numBuffered;
        private int _bufferedEndTime;

        private readonly List<ScriptSpeaker> _speakers = new List<ScriptSpeaker>();

        // Event fired when a speaker should be drawn (Unity subscriber handles visuals)
        public event Action<ScriptSpeaker> OnSpeakerDraw;

        public CGameSound() { }

        // ------------------------------------------------------------------ //
        // Initialization — CG_SoundInit
        // ------------------------------------------------------------------ //

        public void Init()
        {
            if (_numScripts > 0)
            {
                // already parsed — only reset playback state
                ResetPlaybackState();
                return;
            }

            Debug.Log("CGameSound: Initializing Sound Scripts");
            LoadSoundFiles();
        }

        private void ResetPlaybackState()
        {
            for (int i = 0; i < _numSounds; i++)
            {
                _sounds[i].LastPlayed = 0;
                foreach (var sf in _sounds[i].Sounds)
                    sf.Clip = null;   // will be re-loaded on next precache
            }
        }

        // CG_SoundLoadSoundFiles — reads sound/scripts/filelist.txt, then each .sounds file
        private void LoadSoundFiles()
        {
            string fileList = FileSystem.ReadAllText("sound/scripts/filelist.txt");
            if (string.IsNullOrEmpty(fileList))
            {
                Debug.LogWarning("CGameSound: no sound/scripts/filelist.txt found");
                return;
            }

            var files = new List<string>();
            foreach (string line in fileList.Split(new[]{'\n','\r',' ','\t'},
                StringSplitOptions.RemoveEmptyEntries))
                if (line.Length > 0) files.Add(line.Trim());

            // Map-specific sound file is appended implicitly by CG_Init
            // (callers add "<mapname>.sounds" before calling Init)

            foreach (string sf in files)
            {
                string path = $"sound/scripts/{sf}";
                string text = FileSystem.ReadAllText(path);
                if (!string.IsNullOrEmpty(text))
                    ParseSoundsFile(path, text);
            }
        }

        // ------------------------------------------------------------------ //
        // Parser — CG_SoundParseSounds
        // ------------------------------------------------------------------ //

        private void ParseSoundsFile(string filename, string text)
        {
            var tokens = Tokenize(text);
            int pos = 0;

            while (pos < tokens.Count)
            {
                string name = tokens[pos++];
                if (name == "}") continue;

                if (_numScripts >= MAX_SOUND_SCRIPTS)
                {
                    Debug.LogError($"CGameSound: MAX_SOUND_SCRIPTS ({MAX_SOUND_SCRIPTS}) exceeded");
                    return;
                }

                var script  = new SoundScript { Name = name, Index = _numScripts };
                _scripts[_numScripts++] = script;

                // Register in hash table
                int hv = Hash(name);
                script.NextHash = _hashTable[hv];
                _hashTable[hv]  = script;

                // Parse block  {  ... }
                if (pos < tokens.Count && tokens[pos] == "{") pos++;

                SoundScriptSound currentGroup = null;

                while (pos < tokens.Count)
                {
                    string tok = tokens[pos++];
                    if (tok == "}") break;

                    string key = tok.ToLower();
                    if (pos >= tokens.Count) break;

                    switch (key)
                    {
                        case "looping":   script.Looping   = true; break;
                        case "streaming": script.Streaming = true; break;
                        case "random":    script.Random    = true; break;
                        case "volume":
                            if (float.TryParse(tokens[pos++], out float vol))
                                script.Volume = vol / 255f;
                            break;
                        case "mindist":
                            if (float.TryParse(tokens[pos++], out float md))
                                script.MinDist = md;
                            break;
                        case "sound":
                        {
                            // begin a new sound group
                            if (_numSounds >= MAX_SOUND_SCRIPT_SOUNDS)
                            { Debug.LogError("MAX_SOUND_SCRIPT_SOUNDS exceeded"); break; }

                            currentGroup = new SoundScriptSound();
                            _sounds[_numSounds++] = currentGroup;

                            // Append to script's linked list
                            if (script.SoundList == null) script.SoundList = currentGroup;
                            else
                            {
                                var tail = script.SoundList;
                                while (tail.Next != null) tail = tail.Next;
                                tail.Next = currentGroup;
                            }

                            // Parse nested { filename... } block
                            if (pos < tokens.Count && tokens[pos] == "{") pos++;
                            while (pos < tokens.Count)
                            {
                                string fn = tokens[pos++];
                                if (fn == "}") break;
                                currentGroup.Sounds.Add(new SoundScriptFile { Filename = fn });
                            }
                            break;
                        }
                        default:
                            pos++;  // unknown key, skip value
                            break;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ //
        // Precache — CG_SoundScriptPrecache
        // ------------------------------------------------------------------ //

        // Returns 1-based index for fast indexed playback, or 0 if not found.
        public int Precache(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;

            SoundScript s = FindScript(name);
            if (s == null) return 0;

            if (!s.Streaming)
            {
                for (var g = s.SoundList; g != null; g = g.Next)
                {
                    foreach (var sf in g.Sounds)
                    {
                        if (sf.Clip == null)
                            sf.Clip = AudioSystem.RegisterSoundClip(sf.Filename);
                    }
                }
            }

            return s.Index + 1;
        }

        // ------------------------------------------------------------------ //
        // Playback
        // ------------------------------------------------------------------ //

        // CG_SoundPlaySoundScript — returns duration ms if played, 0 if not found
        public int PlaySoundScript(string name, Vector3? origin, int entityNum, bool buffer)
        {
            SoundScript s = FindScript(name);
            if (s == null)
            {
                Debug.LogWarning($"CG_SoundPlaySoundScript: cannot find script '{name}'");
                return 0;
            }

            if (buffer)
            {
                AddBuffered(s);
                return 1;
            }
            return PickAndPlay(s, origin, entityNum);
        }

        // CG_SoundPlayIndexedScript
        public void PlayIndexedScript(int index, Vector3? origin, int entityNum)
        {
            if (index <= 0 || index > _numScripts) return;
            PickAndPlay(_scripts[index - 1], origin, entityNum);
        }

        // ------------------------------------------------------------------ //
        // Buffered queue — CG_AddBufferedSoundScript / CG_UpdateBufferedSoundScripts
        // ------------------------------------------------------------------ //

        public void AddBuffered(SoundScript script)
        {
            if (_numBuffered >= MAX_BUFFERED_SOUNDSCRIPTS) return;
            _buffer[_numBuffered] = new BufferedSoundScript { Script = script };
            if (_numBuffered == 0)
                _bufferedEndTime = Time.frameCount + PickAndPlay(script, null, -1);
            _numBuffered++;
        }

        public void UpdateBuffered(int cgTime)
        {
            if (_numBuffered == 0) return;
            if (cgTime > _bufferedEndTime)
            {
                for (int i = 1; i < _numBuffered; i++)
                    _buffer[i - 1] = _buffer[i];
                _numBuffered--;
                if (_numBuffered > 0)
                    _bufferedEndTime = cgTime + PickAndPlay(_buffer[0].Script, null, -1);
            }
        }

        // ------------------------------------------------------------------ //
        // Script Speakers
        // ------------------------------------------------------------------ //

        // Called from CGameMain configstring parser when CS_SCRIPT_MOVER_NAMES is set
        public void AddSpeaker(ScriptSpeaker sp) => _speakers.Add(sp);

        public void ClearSpeakers() => _speakers.Clear();

        // Per-frame: render visible speakers (subscribers draw icons/lines)
        public void RenderSpeakers()
        {
            foreach (var sp in _speakers)
                OnSpeakerDraw?.Invoke(sp);
        }

        // Toggle speaker active state (CG_ToggleActiveOnScriptSpeaker)
        public void ToggleSpeaker(int index)
        {
            if (index < 0 || index >= _speakers.Count) return;
            _speakers[index].Activated = !_speakers[index].Activated;
        }

        public void SetSpeakerInactive(int index)
        {
            if (index >= 0 && index < _speakers.Count)
                _speakers[index].Activated = false;
        }

        // ------------------------------------------------------------------ //
        // Helpers
        // ------------------------------------------------------------------ //

        private SoundScript FindScript(string name)
        {
            int hv = Hash(name);
            for (var s = _hashTable[hv]; s != null; s = s.NextHash)
                if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
                    return s;
            return null;
        }

        // Pick the least-recently-played sound, load if needed, then play it.
        // Returns estimated duration in ms (0 if nothing played).
        private int PickAndPlay(SoundScript script, Vector3? origin, int entityNum)
        {
            SoundScriptSound oldest = null;
            int oldestTime = int.MaxValue;

            for (var g = script.SoundList; g != null; g = g.Next)
            {
                if (oldest == null || g.LastPlayed < oldestTime)
                {
                    oldestTime = g.LastPlayed;
                    oldest     = g;
                }
            }

            if (oldest == null || oldest.Sounds.Count == 0) return 0;

            int idx = UnityEngine.Random.Range(0, oldest.Sounds.Count);
            var sf  = oldest.Sounds[idx];

            if (sf.Clip == null)
                sf.Clip = AudioSystem.RegisterSoundClip(sf.Filename);

            oldest.LastPlayed = (int)(Time.time * 1000);

            if (sf.Clip != null)
            {
                if (origin.HasValue)
                    AudioSystem.StartSound(origin.Value, entityNum, SoundChannel.Auto,
                        sf.Clip, script.Volume);
                else
                    AudioSystem.StartLocalSound(sf.Clip, script.Volume);

                return (int)(sf.Clip.length * 1000);
            }

            if (script.Streaming && !string.IsNullOrEmpty(sf.Filename))
            {
                AudioSystem.S_StartBackgroundTrack(sf.Filename, sf.Filename);
                return 5000;  // approximate streaming duration
            }

            return 0;
        }

        // djb2-style hash matching the C generateHashValue
        private static int Hash(string name)
        {
            long h = 0;
            for (int i = 0; i < name.Length; i++)
            {
                char c = char.ToLower(name[i]);
                if (c == '.') break;
                if (c == '\\') c = '/';
                h += (long)c * (i + 119);
            }
            return (int)(h & (FILE_HASH_SIZE - 1));
        }

        // Minimal whitespace tokenizer (handles "quoted" strings and {} braces)
        private static List<string> Tokenize(string text)
        {
            var result = new List<string>();
            int i = 0;
            while (i < text.Length)
            {
                while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                if (i >= text.Length) break;

                // Skip // line comments
                if (i + 1 < text.Length && text[i] == '/' && text[i+1] == '/')
                { while (i < text.Length && text[i] != '\n') i++; continue; }

                if (text[i] == '"')
                {
                    i++;
                    int s = i;
                    while (i < text.Length && text[i] != '"') i++;
                    result.Add(text.Substring(s, i - s));
                    if (i < text.Length) i++;
                }
                else if (text[i] == '{' || text[i] == '}')
                { result.Add(text[i].ToString()); i++; }
                else
                {
                    int s = i;
                    while (i < text.Length && !char.IsWhiteSpace(text[i])
                           && text[i] != '{' && text[i] != '}') i++;
                    result.Add(text.Substring(s, i - s));
                }
            }
            return result;
        }
    }
}
