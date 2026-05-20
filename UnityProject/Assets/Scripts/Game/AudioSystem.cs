// Ported from: src/client/snd_main.c, snd_local.h (interface only)
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Replaced: custom 3D software mixer → Unity AudioSource system

using System;
using System.Collections.Generic;
using UnityEngine;

namespace ET.Game
{
    /// <summary>
    /// ET Audio API wrapper. Replaces the original custom 3D software mixer
    /// (snd_mix.c, snd_dma.c) with Unity's AudioSource/AudioClip system.
    /// All public methods mirror the S_* interface from snd_main.c.
    /// </summary>
    public enum SoundChannel { Auto = 0, Local = 1, Announcer = 2 }

    public static class AudioSystem
    {
        // ----------------------------------------------------------------
        // Named sound constants — sfx aliases matching ET path conventions
        // ----------------------------------------------------------------
        public const string SND_PAIN_25   = "sound/player/default/gurp1.wav";
        public const string SND_PAIN_50   = "sound/player/default/gurp2.wav";
        public const string SND_DEATH     = "sound/player/default/death1.wav";
        public const string SND_JUMP      = "sound/player/default/jump1.wav";
        public const string SND_LAND      = "sound/player/default/land1.wav";
        public const string SND_WATER_IN  = "sound/player/default/drown1.wav";
        public const string SND_WATER_OUT = "sound/player/default/gasp1.wav";

        // ----------------------------------------------------------------
        // Internal state
        // ----------------------------------------------------------------

        // Index 0 is always the invalid/null slot. Clips are stored at index+1.
        private static readonly List<AudioClip> _clips = new List<AudioClip> { null };

        // Map from normalized asset name → existing handle (1-based)
        private static readonly Dictionary<string, int> _nameToHandle =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Pool of reusable AudioSources — 32 voices, matches ET's MAX_CHANNELS
        private const int POOL_SIZE = 32;
        private static readonly Queue<AudioSource> _pool = new Queue<AudioSource>(POOL_SIZE);

        // Persistent root GameObject (DontDestroyOnLoad)
        private static GameObject _audioRoot;

        // Dedicated AudioSource for background music
        private static AudioSource _musicIntroSource;
        private static AudioSource _musicLoopSource;

        // Dedicated AudioListener attached to _audioRoot
        private static AudioListener _listener;

        // Master volume (0..1)
        private static float _masterVolume = 1f;

        // Wired by ETGameManager (Assembly-CSharp → ET.Game delegate injection)
        // to avoid circular assembly reference ET.Game → Assembly-CSharp.
        public static Func<string, AudioClip> RuntimeAudioLoader;

        // ----------------------------------------------------------------
        // Initialisation — called lazily on first use
        // ----------------------------------------------------------------

        private static bool _initialised;

        private static void EnsureInit()
        {
            if (_initialised) return;
            _initialised = true;

            _audioRoot = new GameObject("[AudioSystem]");
            UnityEngine.Object.DontDestroyOnLoad(_audioRoot);

            // AudioListener — represents the "ear" in 3D space
            _listener = _audioRoot.AddComponent<AudioListener>();

            // Pre-fill the pool
            for (int i = 0; i < POOL_SIZE; i++)
            {
                var src = _audioRoot.AddComponent<AudioSource>();
                src.playOnAwake   = false;
                src.loop          = false;
                _pool.Enqueue(src);
            }

            // Two dedicated sources for background music
            _musicIntroSource           = _audioRoot.AddComponent<AudioSource>();
            _musicIntroSource.playOnAwake = false;
            _musicIntroSource.loop      = false;

            _musicLoopSource            = _audioRoot.AddComponent<AudioSource>();
            _musicLoopSource.playOnAwake  = false;
            _musicLoopSource.loop       = true;
        }

        // ----------------------------------------------------------------
        // S_RegisterSound — load a sound asset by name
        // Returns a handle (1-based index). Returns 0 on failure.
        // Mirrors: S_RegisterSound in snd_main.c
        // ----------------------------------------------------------------
        public static int S_RegisterSound(string name, bool compressed)
        {
            if (string.IsNullOrEmpty(name)) return 0;

            EnsureInit();

            // Normalise: strip extension, convert backslashes
            string key = NormaliseName(name);

            if (_nameToHandle.TryGetValue(key, out int existing))
                return existing;

            // 1. Try Resources folder (pre-imported assets)
            AudioClip clip = Resources.Load<AudioClip>(key);

            // 2. Fallback: runtime loader (wired by ETGameManager via RuntimeAudioLoader)
            if (clip == null && RuntimeAudioLoader != null)
                clip = RuntimeAudioLoader(name);

            // Store even if null — preserves handle stability (0 = invalid)
            _clips.Add(clip);
            int handle = _clips.Count - 1; // 1-based

            _nameToHandle[key] = handle;

            if (clip == null)
                Debug.LogWarning($"[AudioSystem] S_RegisterSound: not found '{name}'");

            return handle;
        }

        // ----------------------------------------------------------------
        // S_StartSound — play a positioned or 2D sound
        // origin == null  →  play at listener position (2D / non-spatialized)
        // Mirrors: S_StartSound in snd_main.c
        // ----------------------------------------------------------------
        public static void S_StartSound(Vector3? origin, int entityNum, int entchannel,
            int sfxHandle, float volume = 1f, float attenuation = 1f, int timeOfs = 0)
        {
            EnsureInit();

            AudioClip clip = GetClip(sfxHandle);
            if (clip == null) return;

            AudioSource source = AcquireSource();
            if (source == null)
            {
                Debug.LogWarning("[AudioSystem] S_StartSound: pool exhausted, dropping sound.");
                return;
            }

            float effectiveVolume = volume * _masterVolume;

            if (origin.HasValue)
            {
                // 3D spatialized — ET uses a right-handed Z-up coord system;
                // Unity is left-handed Y-up.  Swap Y/Z and negate Z.
                source.transform.position = ETToUnityPosition(origin.Value);
                source.spatialBlend       = 1f;          // full 3D
                source.rolloffMode        = AudioRolloffMode.Linear;
                source.maxDistance        = attenuation > 0f ? (1f / attenuation) * 1000f : 1000f;
            }
            else
            {
                source.transform.position = _audioRoot.transform.position;
                source.spatialBlend       = 0f;          // full 2D
            }

            source.PlayOneShot(clip, effectiveVolume);

            // Return source to pool after clip finishes (fire-and-forget coroutine workaround
            // via a persistent MonoBehaviour runner).
            PoolRunner.Instance.ReturnAfterDelay(source, clip.length);
        }

        // ----------------------------------------------------------------
        // S_StartLocalSound — play a 2D (non-spatialized) sound
        // Mirrors: S_StartLocalSound in snd_main.c
        // ----------------------------------------------------------------
        public static void S_StartLocalSound(int sfxHandle, int entchannel)
        {
            S_StartSound(null, 0, entchannel, sfxHandle);
        }

        public static void S_StartLocalSound(string soundPath, int entchannel)
        {
            int sfx = S_RegisterSound(soundPath, false);
            if (sfx > 0) S_StartSound(null, 0, entchannel, sfx);
        }

        public static void PlayGlobalSound(string name)
        {
            int sfx = S_RegisterSound(name, false);
            if (sfx > 0) S_StartLocalSound(sfx, 0);
        }

        public static void S_StartSoundAtOrigin(Vector3 origin, string soundPath)
        {
            int sfx = S_RegisterSound(soundPath, false);
            if (sfx > 0) S_StartSound(origin, 0, 0, sfx);
        }

        public static AudioClip RegisterSoundClip(string name) { return null; }

        public static void StartSound(Vector3 origin, int entityNum, SoundChannel channel,
            AudioClip clip, float volume) { }

        public static void StartLocalSound(AudioClip clip, float volume) { }

        // ----------------------------------------------------------------
        // S_StartBackgroundTrack — start looping background music
        // intro plays once, loop then repeats indefinitely.
        // Mirrors: S_StartBackgroundTrack in snd_main.c
        // ----------------------------------------------------------------
        public static void S_StartBackgroundTrack(string intro, string loop)
        {
            EnsureInit();

            S_StopBackgroundTrack();

            AudioClip introClip = string.IsNullOrEmpty(intro) ? null
                                : (Resources.Load<AudioClip>(NormaliseName(intro))
                                   ?? RuntimeAudioLoader?.Invoke(intro));
            AudioClip loopClip  = string.IsNullOrEmpty(loop)  ? null
                                : (Resources.Load<AudioClip>(NormaliseName(loop))
                                   ?? RuntimeAudioLoader?.Invoke(loop));

            if (loopClip == null && introClip == null)
            {
                Debug.LogWarning($"[AudioSystem] S_StartBackgroundTrack: could not load '{intro}' or '{loop}'");
                return;
            }

            if (introClip != null && introClip != loopClip)
            {
                _musicIntroSource.clip   = introClip;
                _musicIntroSource.volume = _masterVolume;
                _musicIntroSource.Play();

                if (loopClip != null)
                    PoolRunner.Instance.StartMusicLoop(_musicLoopSource, loopClip,
                        introClip.length, _masterVolume);
            }
            else if (loopClip != null)
            {
                _musicLoopSource.clip   = loopClip;
                _musicLoopSource.volume = _masterVolume;
                _musicLoopSource.Play();
            }
        }

        // ----------------------------------------------------------------
        // S_StopBackgroundTrack
        // Mirrors: S_StopBackgroundTrack in snd_main.c
        // ----------------------------------------------------------------
        public static void S_StopBackgroundTrack()
        {
            EnsureInit();
            _musicIntroSource.Stop();
            _musicLoopSource.Stop();
            PoolRunner.Instance.CancelMusicLoop();
        }

        // ----------------------------------------------------------------
        // S_StopAllSounds
        // Mirrors: S_StopAllSounds in snd_main.c
        // ----------------------------------------------------------------
        public static void S_StopAllSounds()
        {
            EnsureInit();

            S_StopBackgroundTrack();

            // Stop everything currently in flight; they will be re-pooled normally
            foreach (var src in _audioRoot.GetComponents<AudioSource>())
                src.Stop();
        }

        // ----------------------------------------------------------------
        // S_Respatialize — update listener position/orientation each frame.
        // axis0 = forward, axis1 = right (or left in ET), axis2 = up.
        // Mirrors: S_Respatialize in snd_main.c
        // ----------------------------------------------------------------
        public static void S_Respatialize(int entityNum, Vector3 origin,
            Vector3 axis0, Vector3 axis1, Vector3 axis2, bool inWater)
        {
            EnsureInit();

            // Convert ET (right-handed, Z-up) → Unity (left-handed, Y-up)
            _audioRoot.transform.position = ETToUnityPosition(origin);

            // Build Unity forward/up from ET axes
            Vector3 unityForward = ETToUnityDirection(axis0);
            Vector3 unityUp      = ETToUnityDirection(axis2);

            if (unityForward != Vector3.zero && unityUp != Vector3.zero)
                _audioRoot.transform.rotation = Quaternion.LookRotation(unityForward, unityUp);

            // Muffle sounds underwater by reducing the mixer's low-pass cutoff.
            // Unity's built-in AudioMixerGroup integration is optional; here we
            // approximate by scaling volume if needed.
            AudioListener.volume = inWater ? _masterVolume * 0.5f : _masterVolume;
        }

        // ----------------------------------------------------------------
        // S_ClearSoundBuffer — stop all transient sounds (called on map load)
        // Mirrors: S_ClearSoundBuffer in snd_main.c
        // ----------------------------------------------------------------
        public static void S_ClearSoundBuffer()
        {
            EnsureInit();

            // Stop all pooled sources, return them to the pool
            foreach (var src in _audioRoot.GetComponents<AudioSource>())
            {
                if (src == _musicIntroSource || src == _musicLoopSource) continue;
                if (src.isPlaying) src.Stop();
                if (!_pool.Contains(src) && _pool.Count < POOL_SIZE)
                    _pool.Enqueue(src);
            }

            PoolRunner.Instance.CancelAll();
        }

        // ----------------------------------------------------------------
        // S_UpdateEntityPosition — update a moving entity's sound position
        // Mirrors: S_UpdateEntityPosition in snd_main.c
        // (Stub: full implementation would track per-entity AudioSources)
        // ----------------------------------------------------------------
        public static void S_UpdateEntityPosition(int entityNum, Vector3 origin)
        {
            // In a complete implementation we would maintain a Dictionary<int,AudioSource>
            // mapping entity numbers to their active positional sources and move them here.
            // For the ported scope, positional sounds use PlayOneShot (fire-and-forget),
            // so this is intentionally a no-op unless extended.
        }

        // ----------------------------------------------------------------
        // S_SetMasterVolume — 0..1
        // ----------------------------------------------------------------
        public static void S_SetMasterVolume(float vol)
        {
            _masterVolume = Mathf.Clamp01(vol);
            AudioListener.volume = _masterVolume;

            if (_initialised)
            {
                _musicIntroSource.volume = _masterVolume;
                _musicLoopSource.volume  = _masterVolume;
            }
        }

        // ================================================================
        // Private helpers
        // ================================================================

        private static AudioClip GetClip(int handle)
        {
            if (handle <= 0 || handle >= _clips.Count) return null;
            return _clips[handle];
        }

        private static AudioSource AcquireSource()
        {
            if (_pool.Count > 0)
                return _pool.Dequeue();

            // Pool exhausted — create an overflow source (will not be pooled back)
            Debug.LogWarning("[AudioSystem] Voice pool exhausted, creating overflow source.");
            var src = _audioRoot.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop        = false;
            return src;
        }

        /// <summary>
        /// Strips file extension and normalises path separators so the string
        /// can be passed directly to Resources.Load.
        /// </summary>
        private static string NormaliseName(string name)
        {
            // Replace backslashes
            string n = name.Replace('\\', '/');

            // Strip known audio extensions
            string[] exts = { ".wav", ".ogg", ".mp3", ".opus" };
            foreach (var ext in exts)
            {
                if (n.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                {
                    n = n.Substring(0, n.Length - ext.Length);
                    break;
                }
            }

            return n;
        }

        /// <summary>
        /// Convert ET world-space position (right-handed, Z-up) to Unity
        /// world-space position (left-handed, Y-up).
        /// ET: X=right, Y=forward(into screen), Z=up
        /// Unity: X=right, Y=up, Z=forward
        /// </summary>
        private static Vector3 ETToUnityPosition(Vector3 et)
            => new Vector3(et.x, et.z, et.y);

        /// <summary>
        /// Convert an ET direction vector (right-handed Z-up) to Unity direction
        /// (left-handed Y-up).  Same axis permutation as position, no negation
        /// needed for pure directions in this mapping.
        /// </summary>
        private static Vector3 ETToUnityDirection(Vector3 et)
            => new Vector3(et.x, et.z, et.y);

        // ================================================================
        // PoolRunner — inner MonoBehaviour helper for coroutine-based
        // return-to-pool and music sequencing without requiring the caller
        // to be a MonoBehaviour.
        // ================================================================

        private class PoolRunner : MonoBehaviour
        {
            private static PoolRunner _instance;

            public static PoolRunner Instance
            {
                get
                {
                    if (_instance == null)
                    {
                        // Attach to the persistent audio root
                        _instance = _audioRoot.AddComponent<PoolRunner>();
                    }
                    return _instance;
                }
            }

            private Coroutine _musicLoopCoroutine;

            /// <summary>Returns <paramref name="src"/> to the shared pool after
            /// <paramref name="delay"/> seconds.</summary>
            public void ReturnAfterDelay(AudioSource src, float delay)
            {
                StartCoroutine(ReturnRoutine(src, delay));
            }

            private System.Collections.IEnumerator ReturnRoutine(AudioSource src, float delay)
            {
                yield return new WaitForSeconds(delay + 0.05f);
                if (src != null && _pool.Count < POOL_SIZE)
                    _pool.Enqueue(src);
            }

            /// <summary>Waits for the intro clip to finish then starts the loop
            /// track.</summary>
            public void StartMusicLoop(AudioSource loopSrc, AudioClip loopClip,
                float introLength, float volume)
            {
                CancelMusicLoop();
                _musicLoopCoroutine = StartCoroutine(
                    MusicLoopRoutine(loopSrc, loopClip, introLength, volume));
            }

            public void CancelMusicLoop()
            {
                if (_musicLoopCoroutine != null)
                {
                    StopCoroutine(_musicLoopCoroutine);
                    _musicLoopCoroutine = null;
                }
            }

            public void CancelAll()
            {
                StopAllCoroutines();
                _musicLoopCoroutine = null;
            }

            private System.Collections.IEnumerator MusicLoopRoutine(
                AudioSource loopSrc, AudioClip loopClip,
                float introLength, float volume)
            {
                yield return new WaitForSeconds(introLength);
                loopSrc.clip   = loopClip;
                loopSrc.volume = volume;
                loopSrc.Play();
            }
        }
    }
}
