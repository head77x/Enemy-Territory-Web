using System;
using System.Collections.Generic;
using UnityEngine;
using ET.Core;
using ET.Network;
using ET.Game;

namespace ET.Client
{
    // -------------------------------------------------------------------------
    // cg_atmospheric.c
    // -------------------------------------------------------------------------

    public enum AtmosType { None = 0, Rain = 1, Snow = 2 }

    public static class CGameAtmospheric
    {
        private static AtmosType      _atmosType        = AtmosType.None;
        private static float          _atmosDensity     = 0.3f;
        private static float          _atmosWindAngle   = 0f;
        private static float          _atmosWindSpeed   = 1f;
        private static GameObject     _atmosRoot;
        private static ParticleSystem _atmosParticles;

        public static void CG_InitAtmosphericEffects()
        {
            _atmosType      = AtmosType.None;
            _atmosDensity   = 0.3f;
            _atmosWindAngle = 0f;
            _atmosWindSpeed = 1f;

            if (_atmosRoot != null)
                UnityEngine.Object.Destroy(_atmosRoot);
            _atmosRoot      = null;
            _atmosParticles = null;
        }

        // Parses a configstring produced by et_atmos / atmospheric CVARs.
        // Accepted formats:
        //   "rain [density]"
        //   "snow [density] [wind <angle> <speed>]"
        //   "none"
        public static void CG_EffectParse(string effectStr)
        {
            if (string.IsNullOrEmpty(effectStr))
            {
                CG_InitAtmosphericEffects();
                return;
            }

            string[] tokens = effectStr.Trim().Split(new char[]{' ','\t'}, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                return;

            switch (tokens[0].ToLowerInvariant())
            {
                case "rain":
                    _atmosType = AtmosType.Rain;
                    break;
                case "snow":
                    _atmosType = AtmosType.Snow;
                    break;
                default:
                    _atmosType = AtmosType.None;
                    if (_atmosParticles != null)
                        _atmosParticles.Stop();
                    return;
            }

            if (tokens.Length > 1)
                float.TryParse(tokens[1], System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out _atmosDensity);

            for (int i = 2; i < tokens.Length - 2; i++)
            {
                if (tokens[i].Equals("wind", StringComparison.OrdinalIgnoreCase))
                {
                    float.TryParse(tokens[i + 1], System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out _atmosWindAngle);
                    float.TryParse(tokens[i + 2], System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out _atmosWindSpeed);
                }
            }

            SetupParticles();
        }

        public static void CG_AddAtmosphericEffects()
        {
            if (_atmosType == AtmosType.None || _atmosParticles == null)
                return;

            if (Camera.main == null)
                return;

            // Keep the emitter centred above the camera so particles always
            // fall through the player's view without requiring a large world-space
            // emitter box.
            Vector3 emitPos = Camera.main.transform.position + Vector3.up * 20f;
            _atmosRoot.transform.position = emitPos;
        }

        private static void SetupParticles()
        {
            if (_atmosRoot == null)
            {
                _atmosRoot = new GameObject("CG_AtmosRoot");
                UnityEngine.Object.DontDestroyOnLoad(_atmosRoot);
                _atmosParticles = _atmosRoot.AddComponent<ParticleSystem>();
            }

            _atmosParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _atmosParticles.main;
            main.maxParticles = 1000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = _atmosParticles.emission;
            emission.rateOverTime = 200f * Mathf.Clamp01(_atmosDensity);

            var shape = _atmosParticles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(60f, 1f, 60f);

            float windRad = _atmosWindAngle * Mathf.Deg2Rad;
            Vector3 windDir = new Vector3(Mathf.Sin(windRad), 0f, Mathf.Cos(windRad)) * _atmosWindSpeed;

            if (_atmosType == AtmosType.Rain)
            {
                main.startLifetime  = 0.8f;
                main.startSpeed     = 25f;
                main.startSize      = 0.06f;
                main.startColor     = new Color(0.55f, 0.65f, 0.80f, 0.70f);

                var vel = _atmosParticles.velocityOverLifetime;
                vel.enabled = true;
                vel.space   = ParticleSystemSimulationSpace.World;
                vel.x       = new ParticleSystem.MinMaxCurve(windDir.x);
                vel.y       = new ParticleSystem.MinMaxCurve(-25f);
                vel.z       = new ParticleSystem.MinMaxCurve(windDir.z);

                var renderer = _atmosParticles.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.04f;
                renderer.lengthScale   = 2.0f;
            }
            else // Snow
            {
                main.startLifetime  = new ParticleSystem.MinMaxCurve(3f, 6f);
                main.startSpeed     = new ParticleSystem.MinMaxCurve(0.5f, 2f);
                main.startSize      = new ParticleSystem.MinMaxCurve(0.05f, 0.18f);
                main.startColor     = new Color(0.95f, 0.97f, 1.00f, 0.85f);

                var vel = _atmosParticles.velocityOverLifetime;
                vel.enabled = true;
                vel.space   = ParticleSystemSimulationSpace.World;
                // Gentle sinusoidal swirl baked as a random range.
                vel.x = new ParticleSystem.MinMaxCurve(-0.4f + windDir.x, 0.4f + windDir.x);
                vel.y = new ParticleSystem.MinMaxCurve(-2f, -0.5f);
                vel.z = new ParticleSystem.MinMaxCurve(-0.4f + windDir.z, 0.4f + windDir.z);
            }

            _atmosParticles.Play();
        }
    }

    // -------------------------------------------------------------------------
    // cg_particles.c
    // -------------------------------------------------------------------------

    public enum ParticleType
    {
        Spark     = 0,
        Smoke     = 1,
        Fire      = 2,
        Debris    = 3,
        Water     = 4,
        Snowflake = 5,
        Blood     = 6,
        Shrapnel  = 7,
        Count     = 8,
    }

    public class ParticleInfo
    {
        public Vector3      Origin;
        public Vector3      Velocity;
        public Color        StartColor, EndColor;
        public float        StartSize,  EndSize;
        public float        SpawnTime,  LifeTime;
        public ParticleType Type;
        public bool         Active;
    }

    public static class CGameParticles
    {
        public const int MAX_PARTICLES = 4096;

        private static readonly ParticleInfo[]       _particles     = new ParticleInfo[MAX_PARTICLES];
        private static          ParticleSystem        _particleSystem;
        private static          GameObject            _particleRoot;
        private static readonly ParticleSystem.Particle[] _scratch  = new ParticleSystem.Particle[MAX_PARTICLES];

        public static void CG_InitParticles()
        {
            for (int i = 0; i < MAX_PARTICLES; i++)
            {
                if (_particles[i] == null)
                    _particles[i] = new ParticleInfo();
                _particles[i].Active = false;
            }

            if (_particleRoot == null)
            {
                _particleRoot = new GameObject("CG_ParticleRoot");
                UnityEngine.Object.DontDestroyOnLoad(_particleRoot);
                _particleSystem = _particleRoot.AddComponent<ParticleSystem>();
                var main = _particleSystem.main;
                main.maxParticles    = MAX_PARTICLES;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                // Emission handled manually via Emit() calls.
                var emission = _particleSystem.emission;
                emission.rateOverTime = 0f;
                _particleSystem.Stop();
            }
        }

        private static int CG_AllocParticle()
        {
            for (int i = 0; i < MAX_PARTICLES; i++)
            {
                if (!_particles[i].Active)
                    return i;
            }
            return -1;
        }

        public static void CG_FreeParticle(int idx)
        {
            if (idx >= 0 && idx < MAX_PARTICLES)
                _particles[idx].Active = false;
        }

        public static int CG_AddParticle(Vector3 origin, Vector3 velocity,
            Color startColor, Color endColor, float startSize, float endSize,
            float lifeTime, ParticleType type)
        {
            int idx = CG_AllocParticle();
            if (idx < 0)
                return -1;

            var p = _particles[idx];
            p.Origin     = origin;
            p.Velocity   = velocity;
            p.StartColor = startColor;
            p.EndColor   = endColor;
            p.StartSize  = startSize;
            p.EndSize    = endSize;
            p.SpawnTime  = Time.time;
            p.LifeTime   = lifeTime;
            p.Type       = type;
            p.Active     = true;
            return idx;
        }

        public static void CG_AddParticles()
        {
            if (_particleSystem == null)
                return;

            float now = Time.time;
            float dt  = Time.deltaTime;

            for (int i = 0; i < MAX_PARTICLES; i++)
            {
                var p = _particles[i];
                if (!p.Active)
                    continue;

                float age = now - p.SpawnTime;
                if (age >= p.LifeTime)
                {
                    p.Active = false;
                    continue;
                }

                float t = age / p.LifeTime;

                // Gravity applied to non-smoke/fire types.
                bool hasGravity = p.Type != ParticleType.Smoke && p.Type != ParticleType.Fire;
                if (hasGravity)
                    p.Velocity += Physics.gravity * dt;

                p.Origin += p.Velocity * dt;

                var ep = new ParticleSystem.EmitParams
                {
                    position    = ETToUnity(p.Origin),
                    startColor  = Color.Lerp(p.StartColor, p.EndColor, t),
                    startSize   = Mathf.Lerp(p.StartSize,  p.EndSize,  t),
                    startLifetime = Time.deltaTime + 0.001f,  // single-frame immediate particle
                    remainingLifetime = Time.deltaTime + 0.001f,
                    velocity    = ETToUnity(p.Velocity),
                };
                _particleSystem.Emit(ep, 1);
            }
        }

        // Spawn helpers --------------------------------------------------------

        public static void CG_SpawnExplosionParticles(Vector3 origin, float radius)
        {
            int smokeCount    = 12;
            int sparkCount    = 20;
            int shrapnelCount = 8;

            for (int i = 0; i < smokeCount; i++)
            {
                Vector3 vel = UnityEngine.Random.insideUnitSphere * 4f + Vector3.up * 2f;
                CG_AddParticle(origin, vel,
                    new Color(0.4f, 0.4f, 0.4f, 0.9f), new Color(0.2f, 0.2f, 0.2f, 0f),
                    0.4f, 1.4f, UnityEngine.Random.Range(1.2f, 2.5f), ParticleType.Smoke);
            }

            for (int i = 0; i < sparkCount; i++)
            {
                Vector3 vel = UnityEngine.Random.insideUnitSphere.normalized
                            * UnityEngine.Random.Range(6f, 14f);
                CG_AddParticle(origin, vel,
                    new Color(1f, 0.8f, 0.1f, 1f), new Color(1f, 0.2f, 0f, 0f),
                    0.05f, 0.02f, UnityEngine.Random.Range(0.4f, 1.0f), ParticleType.Spark);
            }

            for (int i = 0; i < shrapnelCount; i++)
            {
                Vector3 vel = UnityEngine.Random.insideUnitSphere.normalized
                            * UnityEngine.Random.Range(3f, radius * 1.5f);
                CG_AddParticle(origin, vel,
                    new Color(0.6f, 0.55f, 0.5f, 1f), new Color(0.3f, 0.3f, 0.3f, 0.5f),
                    0.12f, 0.10f, UnityEngine.Random.Range(0.6f, 1.4f), ParticleType.Shrapnel);
            }
        }

        public static void CG_SpawnBloodParticles(Vector3 origin, Vector3 normal)
        {
            for (int i = 0; i < 16; i++)
            {
                Vector3 vel = (normal + UnityEngine.Random.insideUnitSphere * 0.6f).normalized
                            * UnityEngine.Random.Range(1.5f, 5f);
                CG_AddParticle(origin, vel,
                    new Color(0.75f, 0.04f, 0.04f, 1f), new Color(0.5f, 0.02f, 0.02f, 0f),
                    0.08f, 0.04f, UnityEngine.Random.Range(0.3f, 0.9f), ParticleType.Blood);
            }
        }

        public static void CG_SpawnSmokeParticle(Vector3 origin, float density)
        {
            int count = Mathf.RoundToInt(Mathf.Clamp(density * 8f, 1f, 12f));
            for (int i = 0; i < count; i++)
            {
                Vector3 vel = new Vector3(
                    UnityEngine.Random.Range(-0.5f, 0.5f),
                    UnityEngine.Random.Range(1.0f, 2.5f),
                    UnityEngine.Random.Range(-0.5f, 0.5f));
                CG_AddParticle(origin, vel,
                    new Color(0.55f, 0.55f, 0.55f, 0.7f), new Color(0.7f, 0.7f, 0.7f, 0f),
                    0.3f, 1.0f, UnityEngine.Random.Range(1.5f, 3.5f), ParticleType.Smoke);
            }
        }

        public static void CG_SpawnFireParticles(Vector3 origin, float intensity)
        {
            int count = Mathf.RoundToInt(Mathf.Clamp(intensity * 12f, 2f, 20f));
            for (int i = 0; i < count; i++)
            {
                Vector3 vel = new Vector3(
                    UnityEngine.Random.Range(-1f, 1f) * intensity,
                    UnityEngine.Random.Range(3f, 7f)  * intensity,
                    UnityEngine.Random.Range(-1f, 1f) * intensity);
                CG_AddParticle(origin, vel,
                    new Color(1f, 0.6f, 0.05f, 1f), new Color(0.9f, 0.1f, 0f, 0f),
                    0.15f * intensity, 0.02f, UnityEngine.Random.Range(0.3f, 0.8f), ParticleType.Fire);
            }
        }

        public static void CG_SpawnSparkParticles(Vector3 origin, Vector3 normal, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 dir = Vector3.Reflect(
                    -normal + UnityEngine.Random.insideUnitSphere * 0.4f, normal).normalized;
                Vector3 vel = dir * UnityEngine.Random.Range(3f, 10f);
                CG_AddParticle(origin, vel,
                    new Color(1f, 0.9f, 0.5f, 1f), new Color(0.8f, 0.4f, 0f, 0f),
                    0.04f, 0.01f, UnityEngine.Random.Range(0.2f, 0.7f), ParticleType.Spark);
            }
        }

        public static void CG_SpawnDebrisParticles(Vector3 origin, float radius, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 vel = UnityEngine.Random.insideUnitSphere.normalized
                            * UnityEngine.Random.Range(1f, radius * 2f);
                CG_AddParticle(origin, vel,
                    new Color(0.5f, 0.45f, 0.35f, 1f), new Color(0.3f, 0.27f, 0.2f, 0.6f),
                    UnityEngine.Random.Range(0.08f, 0.25f),
                    UnityEngine.Random.Range(0.06f, 0.20f),
                    UnityEngine.Random.Range(0.8f, 2.0f), ParticleType.Debris);
            }
        }

        public static void CG_SpawnWaterSplash(Vector3 origin)
        {
            for (int i = 0; i < 14; i++)
            {
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float speed = UnityEngine.Random.Range(1f, 5f);
                Vector3 vel = new Vector3(
                    Mathf.Cos(angle) * speed,
                    UnityEngine.Random.Range(3f, 8f),
                    Mathf.Sin(angle) * speed);
                CG_AddParticle(origin, vel,
                    new Color(0.6f, 0.75f, 1f, 0.8f), new Color(0.6f, 0.75f, 1f, 0f),
                    0.07f, 0.05f, UnityEngine.Random.Range(0.4f, 1.0f), ParticleType.Water);
            }
        }

        private static Vector3 ETToUnity(Vector3 et) =>
            new Vector3(-et.y, et.z, et.x);
    }

    // -------------------------------------------------------------------------
    // cg_trails.c
    // -------------------------------------------------------------------------

    public enum TrailType { Smoke = 0, Flame = 1, Tracer = 2, RocketSmoke = 3 }

    public class TrailSegment
    {
        public Vector3 Start, End;
        public float   Width;
        public Color   Color;
        public float   Alpha;
        public float   SpawnTime;
        public float   LifeTime;
        public bool    Active;
    }

    public static class CGameTrails
    {
        public const int MAX_TRAIL_SYSTEMS  = 128;
        public const int MAX_TRAIL_SEGMENTS = 32;

        private static readonly TrailSegment[][] _trails      = new TrailSegment[MAX_TRAIL_SYSTEMS][];
        private static readonly int[]            _trailHeads  = new int[MAX_TRAIL_SYSTEMS];
        private static readonly bool[]           _trailActive = new bool[MAX_TRAIL_SYSTEMS];
        private static readonly LineRenderer[]   _renderers   = new LineRenderer[MAX_TRAIL_SYSTEMS];
        private static          GameObject       _trailRoot;

        // Maps entity number → trail system handle so projectiles keep the same trail.
        private static readonly Dictionary<int, int> _entityTrail = new Dictionary<int, int>();

        public static void CG_InitTrails()
        {
            if (_trailRoot == null)
            {
                _trailRoot = new GameObject("CG_TrailRoot");
                UnityEngine.Object.DontDestroyOnLoad(_trailRoot);
            }

            for (int i = 0; i < MAX_TRAIL_SYSTEMS; i++)
            {
                _trailActive[i] = false;
                _trailHeads[i]  = 0;

                if (_trails[i] == null)
                {
                    _trails[i] = new TrailSegment[MAX_TRAIL_SEGMENTS];
                    for (int j = 0; j < MAX_TRAIL_SEGMENTS; j++)
                        _trails[i][j] = new TrailSegment();
                }
                else
                {
                    for (int j = 0; j < MAX_TRAIL_SEGMENTS; j++)
                        _trails[i][j].Active = false;
                }

                if (_renderers[i] == null)
                {
                    var go = new GameObject($"Trail_{i}");
                    go.transform.SetParent(_trailRoot.transform, false);
                    var lr = go.AddComponent<LineRenderer>();
                    lr.positionCount = 0;
                    lr.useWorldSpace = true;
                    lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    lr.receiveShadows = false;
                    _renderers[i] = lr;
                }
                else
                {
                    _renderers[i].positionCount = 0;
                }
            }

            _entityTrail.Clear();
        }

        public static int CG_AllocTrailSystem()
        {
            for (int i = 0; i < MAX_TRAIL_SYSTEMS; i++)
            {
                if (!_trailActive[i])
                {
                    _trailActive[i] = true;
                    _trailHeads[i]  = 0;
                    for (int j = 0; j < MAX_TRAIL_SEGMENTS; j++)
                        _trails[i][j].Active = false;
                    return i;
                }
            }
            return -1;
        }

        public static void CG_FreeTrailSystem(int handle)
        {
            if (handle < 0 || handle >= MAX_TRAIL_SYSTEMS)
                return;
            _trailActive[handle] = false;
            _renderers[handle].positionCount = 0;
        }

        public static void CG_AddTrailSegment(int handle, Vector3 from, Vector3 to,
            float width, Color color, float lifeTime)
        {
            if (handle < 0 || handle >= MAX_TRAIL_SYSTEMS || !_trailActive[handle])
                return;

            int head = _trailHeads[handle];
            var seg  = _trails[handle][head];

            seg.Start     = from;
            seg.End       = to;
            seg.Width     = width;
            seg.Color     = color;
            seg.Alpha     = color.a;
            seg.SpawnTime = Time.time;
            seg.LifeTime  = lifeTime;
            seg.Active    = true;

            _trailHeads[handle] = (head + 1) % MAX_TRAIL_SEGMENTS;
        }

        public static void CG_DrawTrails()
        {
            float now = Time.time;

            for (int i = 0; i < MAX_TRAIL_SYSTEMS; i++)
            {
                if (!_trailActive[i])
                {
                    if (_renderers[i].positionCount > 0)
                        _renderers[i].positionCount = 0;
                    continue;
                }

                // Collect live segments (ring buffer, oldest → newest).
                int liveCount = 0;
                var positions = new Vector3[MAX_TRAIL_SEGMENTS + 1];
                var colours   = new GradientColorKey[MAX_TRAIL_SEGMENTS + 1];
                var alphas    = new GradientAlphaKey[MAX_TRAIL_SEGMENTS + 1];

                int head = _trailHeads[i];
                for (int k = 0; k < MAX_TRAIL_SEGMENTS; k++)
                {
                    int idx = (head + k) % MAX_TRAIL_SEGMENTS;
                    var seg = _trails[i][idx];
                    if (!seg.Active)
                        continue;

                    float age = now - seg.SpawnTime;
                    if (age >= seg.LifeTime)
                    {
                        seg.Active = false;
                        continue;
                    }

                    float t     = age / seg.LifeTime;
                    float alpha = seg.Alpha * (1f - t);

                    if (liveCount == 0)
                    {
                        positions[liveCount] = ETToUnity(seg.Start);
                        colours[liveCount]   = new GradientColorKey(seg.Color, 0f);
                        alphas[liveCount]    = new GradientAlphaKey(alpha, 0f);
                        liveCount++;
                    }

                    positions[liveCount] = ETToUnity(seg.End);
                    float gPos = (float)liveCount / MAX_TRAIL_SEGMENTS;
                    colours[liveCount] = new GradientColorKey(seg.Color, gPos);
                    alphas[liveCount]  = new GradientAlphaKey(alpha * 0.5f, gPos);
                    liveCount++;
                }

                var lr = _renderers[i];
                if (liveCount < 2)
                {
                    lr.positionCount = 0;
                    continue;
                }

                lr.positionCount = liveCount;
                lr.SetPositions(positions);

                var seg0     = _trails[i][head];
                lr.startWidth = seg0.Active ? seg0.Width : 0.05f;
                lr.endWidth   = lr.startWidth * 0.2f;

                var grad = new Gradient();
                var ck   = new GradientColorKey[Mathf.Min(liveCount, 8)];
                var ak   = new GradientAlphaKey[Mathf.Min(liveCount, 8)];
                int step = Mathf.Max(1, liveCount / 8);
                for (int g = 0; g < ck.Length; g++)
                {
                    int src = Mathf.Min(g * step, liveCount - 1);
                    ck[g] = colours[src];
                    ak[g] = alphas[src];
                    ck[g].time = (float)g / (ck.Length - 1);
                    ak[g].time = (float)g / (ak.Length - 1);
                }
                grad.SetKeys(ck, ak);
                lr.colorGradient = grad;
            }
        }

        public static void CG_AddProjectileTrail(int entityNum, Vector3 origin, int weaponNum)
        {
            // weaponNum 5 = rocket, 7 = flamethrower in ET weapon table.
            const int WP_FLAMETHROWER = 7;
            const int WP_ROCKET       = 5;

            if (!_entityTrail.TryGetValue(entityNum, out int handle) || handle < 0)
            {
                handle = CG_AllocTrailSystem();
                if (handle < 0)
                    return;
                _entityTrail[entityNum] = handle;
            }

            // We only have the current tip; the segment endpoint is updated next frame.
            // Store as a zero-length marker so DrawTrails can connect them.
            if (weaponNum == WP_ROCKET)
                CG_AddRocketTrail(origin, origin);
            else if (weaponNum == WP_FLAMETHROWER)
                CG_AddFlamethrowerTrail(origin, origin);
        }

        public static void CG_AddRocketTrail(Vector3 start, Vector3 end)
        {
            int h = CG_AllocTrailSystem();
            if (h < 0)
                return;
            CG_AddTrailSegment(h, start, end, 0.18f, new Color(0.7f, 0.7f, 0.7f, 0.8f), 0.8f);
            // Spawn a few smoke particles along the trail.
            CGameParticles.CG_SpawnSmokeParticle(start, 0.4f);
        }

        public static void CG_AddFlamethrowerTrail(Vector3 start, Vector3 end)
        {
            int h = CG_AllocTrailSystem();
            if (h < 0)
                return;
            CG_AddTrailSegment(h, start, end, 0.30f, new Color(1f, 0.45f, 0.05f, 0.9f), 0.35f);
            CGameParticles.CG_SpawnFireParticles(end, 0.8f);
        }

        public static void CG_AddTracerEffect(Vector3 start, Vector3 end)
        {
            int h = CG_AllocTrailSystem();
            if (h < 0)
                return;
            // Tracers are bright, short-lived streaks — no particle smoke needed.
            CG_AddTrailSegment(h, start, end, 0.04f, new Color(1f, 0.95f, 0.6f, 1f), 0.12f);
        }

        private static Vector3 ETToUnity(Vector3 et) =>
            new Vector3(-et.y, et.z, et.x);
    }
}
