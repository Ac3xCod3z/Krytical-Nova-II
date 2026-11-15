using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

namespace KryticalNova.Rendering
{
    /// <summary>
    /// The swing of every <see cref="DangleBones"/> at once, in jobs (perf round 3, chunk R3-4: the per-character
    /// LateUpdate cost 20.3 ms at 200 characters in view). Each frame: one job reads every dangle and body-capsule bone,
    /// one Burst job per character runs the same steps <see cref="DangleBones"/> always ran (gravity, lag, spring back,
    /// length, capsules, then turning each bone at its swung children), and one job writes the turned bones back. The
    /// choice of who swings this frame (level of detail, seen, half rate, teleport) stays on the character.
    /// </summary>
    internal sealed class DangleBonesBatch
    {
        internal enum Mode : byte { Skip, Reset, Simulate, Hold }

        internal struct CharData
        {
            public int pStart, pCount, capStart, capCount, reachStart;
            public Mode mode;
            public byte rest;
            public float dt, prevDt, scale;
            public float3 moved;   // Simulate: the body's move this step; Hold: the shift the pieces are carried by
            public float3 gravity; // Physics.gravity * dt * dt
        }

        private const float Step60 = 1f / 60f;
        private const float SideReach = 3f;

        private readonly Allocator alloc;
        internal readonly List<DangleBones> members = new List<DangleBones>();
        private bool built;

        // Per transform.
        private TransformAccessArray taa;
        private NativeArray<int> taChar, taParticle;
        private NativeArray<float4x4> readM;
        private NativeArray<quaternion> readRot, readLocalRot;
        private NativeArray<float3> readLocalPos, readLocalScale;
        // Per character.
        internal NativeArray<CharData> chars;
        // Per particle.
        private NativeArray<int> parentG, taIndex, leafG, firstChildG, nextSiblingG;
        private NativeArray<float3> tipLocal, restPos, pos, prev, target;
        private NativeArray<quaternion> restRot, newRot, outLocal;
        private NativeArray<float4x4> curM;
        private NativeArray<float4> prof; // stiffness, damping, gravity, follow
        private NativeArray<float> profRadius;
        private NativeArray<byte> write;
        // Per capsule.
        private NativeArray<int> capTaA, capTaB;
        private NativeArray<float> capRadius, capExtend;
        private NativeArray<float3> segA, segB;
        private NativeArray<quaternion> capRot;
        // Per particle and capsule.
        private NativeArray<float> reach;
        private NativeArray<float3> side;

        internal DangleBonesBatch(Allocator alloc) { this.alloc = alloc; }

        internal int Count => members.Count;

        /// <summary>Lays out the members' bones; each member's swing state is carried over from its kept arrays.</summary>
        internal void Build()
        {
            Dispose();
            int transforms = 0, particles = 0, caps = 0, reaches = 0;
            var taList = new List<Transform>();
            var taIdx = new Dictionary<Transform, int>();
            foreach (var d in members)
            {
                particles += d.count; caps += d.capsuleCount; reaches += d.count * d.capsuleCount;
            }

            chars = new NativeArray<CharData>(members.Count, alloc);
            parentG = new NativeArray<int>(particles, alloc); taIndex = new NativeArray<int>(particles, alloc);
            leafG = new NativeArray<int>(particles, alloc); firstChildG = new NativeArray<int>(particles, alloc);
            nextSiblingG = new NativeArray<int>(particles, alloc);
            tipLocal = new NativeArray<float3>(particles, alloc); restPos = new NativeArray<float3>(particles, alloc);
            pos = new NativeArray<float3>(particles, alloc); prev = new NativeArray<float3>(particles, alloc);
            target = new NativeArray<float3>(particles, alloc);
            restRot = new NativeArray<quaternion>(particles, alloc); newRot = new NativeArray<quaternion>(particles, alloc);
            outLocal = new NativeArray<quaternion>(particles, alloc);
            curM = new NativeArray<float4x4>(particles, alloc);
            prof = new NativeArray<float4>(particles, alloc); profRadius = new NativeArray<float>(particles, alloc);
            write = new NativeArray<byte>(particles, alloc);
            capTaA = new NativeArray<int>(caps, alloc); capTaB = new NativeArray<int>(caps, alloc);
            capRadius = new NativeArray<float>(caps, alloc); capExtend = new NativeArray<float>(caps, alloc);
            segA = new NativeArray<float3>(caps, alloc); segB = new NativeArray<float3>(caps, alloc);
            capRot = new NativeArray<quaternion>(caps, alloc);
            reach = new NativeArray<float>(reaches, alloc); side = new NativeArray<float3>(reaches, alloc);

            var taCharList = new List<int>(); var taParticleList = new List<int>();
            int p0 = 0, c0 = 0, r0 = 0;
            for (int ci = 0; ci < members.Count; ci++)
            {
                var d = members[ci];
                taIdx.Clear();
                int Ta(Transform t, int particle)
                {
                    if (taIdx.TryGetValue(t, out int at)) return at;
                    at = taList.Count;
                    taList.Add(t); taCharList.Add(ci); taParticleList.Add(particle);
                    taIdx[t] = at;
                    return at;
                }
                for (int i = 0; i < d.count; i++)
                {
                    int g = p0 + i;
                    var b = d.bone[i];
                    taIndex[g] = b != null ? Ta(b, g) : -1;
                    parentG[g] = d.parent[i] >= 0 ? p0 + d.parent[i] : -1;
                    leafG[g] = d.leafOf[i] >= 0 ? p0 + d.leafOf[i] : -1;
                    firstChildG[g] = d.firstChild[i] >= 0 ? p0 + d.firstChild[i] : -1;
                    nextSiblingG[g] = d.nextSibling[i] >= 0 ? p0 + d.nextSibling[i] : -1;
                    tipLocal[g] = d.tipLocal[i]; restPos[g] = d.restPos[i]; restRot[g] = d.restRot[i];
                    pos[g] = d.keptPos[i]; prev[g] = d.keptPrev[i];
                    var pr = d.ProfileOf(i);
                    prof[g] = new float4(pr.stiffness, pr.damping, pr.gravity, pr.follow);
                    profRadius[g] = pr.radius;
                }
                for (int k = 0; k < d.capsuleCount; k++)
                {
                    capTaA[c0 + k] = Ta(d.capA[k], -1); capTaB[c0 + k] = Ta(d.capB[k], -1);
                    capRadius[c0 + k] = d.capRadius[k]; capExtend[c0 + k] = d.capExtend[k];
                }
                for (int r = 0; r < d.count * d.capsuleCount; r++) { reach[r0 + r] = d.reach[r]; side[r0 + r] = d.side[r]; }
                chars[ci] = new CharData { pStart = p0, pCount = d.count, capStart = c0, capCount = d.capsuleCount, reachStart = r0 };
                d.batchIndex = ci; d.batchPStart = p0;
                p0 += d.count; c0 += d.capsuleCount; r0 += d.count * d.capsuleCount;
            }
            transforms = taList.Count;
            taa = new TransformAccessArray(taList.ToArray());
            taChar = new NativeArray<int>(taCharList.ToArray(), alloc);
            taParticle = new NativeArray<int>(taParticleList.ToArray(), alloc);
            readM = new NativeArray<float4x4>(transforms, alloc);
            readRot = new NativeArray<quaternion>(transforms, alloc); readLocalRot = new NativeArray<quaternion>(transforms, alloc);
            readLocalPos = new NativeArray<float3>(transforms, alloc); readLocalScale = new NativeArray<float3>(transforms, alloc);
            built = true;
        }

        /// <summary>Copies every member's swing back into its kept arrays (before a new layout, or after a test step).</summary>
        internal void Keep()
        {
            if (!built) return;
            foreach (var d in members)
            {
                if (d.batchIndex < 0) continue; // new or rebound since: its swing starts again
                int s = d.batchPStart;
                for (int i = 0; i < d.count; i++) { d.keptPos[i] = pos[s + i]; d.keptPrev[i] = prev[s + i]; }
            }
        }

        /// <summary>Runs this frame's swing for the members whose <see cref="CharData.mode"/> is set.</summary>
        internal void Run()
        {
            if (!built || members.Count == 0) return;
            var read = new ReadJob
            {
                chars = chars, taChar = taChar, readM = readM, readRot = readRot, readLocalRot = readLocalRot,
                readLocalPos = readLocalPos, readLocalScale = readLocalScale,
            }.ScheduleReadOnly(taa, 32);
            var sim = new SimJob
            {
                chars = chars, readM = readM, readRot = readRot, readLocalRot = readLocalRot, readLocalPos = readLocalPos,
                readLocalScale = readLocalScale, parentG = parentG, taIndex = taIndex, leafG = leafG,
                firstChildG = firstChildG, nextSiblingG = nextSiblingG, tipLocal = tipLocal, restPos = restPos,
                prof = prof, profRadius = profRadius, capTaA = capTaA, capTaB = capTaB, capRadius = capRadius,
                capExtend = capExtend, reach = reach, side = side, pos = pos, prev = prev, target = target, curM = curM,
                newRot = newRot, outLocal = outLocal, write = write, segA = segA, segB = segB, capRot = capRot,
            }.Schedule(members.Count, 1, read);
            new WriteJob { taParticle = taParticle, write = write, outLocal = outLocal }.Schedule(taa, sim).Complete();
        }

        /// <summary>Puts the bones of the members whose <see cref="CharData.rest"/> is set back in their rest pose.</summary>
        internal void Rest()
        {
            if (!built || members.Count == 0) return;
            new RestJob { chars = chars, taChar = taChar, taParticle = taParticle, restPos = restPos, restRot = restRot }
                .Schedule(taa).Complete();
        }

        internal void Dispose()
        {
            if (!built) return;
            built = false;
            if (taa.isCreated) taa.Dispose();
            taChar.Dispose(); taParticle.Dispose(); readM.Dispose(); readRot.Dispose(); readLocalRot.Dispose();
            readLocalPos.Dispose(); readLocalScale.Dispose(); chars.Dispose();
            parentG.Dispose(); taIndex.Dispose(); leafG.Dispose(); firstChildG.Dispose(); nextSiblingG.Dispose();
            tipLocal.Dispose(); restPos.Dispose(); pos.Dispose(); prev.Dispose(); target.Dispose();
            restRot.Dispose(); newRot.Dispose(); outLocal.Dispose(); curM.Dispose(); prof.Dispose(); profRadius.Dispose();
            write.Dispose(); capTaA.Dispose(); capTaB.Dispose(); capRadius.Dispose(); capExtend.Dispose();
            segA.Dispose(); segB.Dispose(); capRot.Dispose(); reach.Dispose(); side.Dispose();
        }

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
        private struct ReadJob : IJobParallelForTransform
        {
            [ReadOnly] public NativeArray<CharData> chars;
            [ReadOnly] public NativeArray<int> taChar;
            [WriteOnly] public NativeArray<float4x4> readM;
            [WriteOnly] public NativeArray<quaternion> readRot, readLocalRot;
            [WriteOnly] public NativeArray<float3> readLocalPos, readLocalScale;

            public void Execute(int index, TransformAccess t)
            {
                if (!t.isValid || chars[taChar[index]].mode == Mode.Skip) return;
                readM[index] = t.localToWorldMatrix;
                readRot[index] = t.rotation;
                readLocalRot[index] = t.localRotation;
                readLocalPos[index] = t.localPosition;
                readLocalScale[index] = t.localScale;
            }
        }

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
        private struct WriteJob : IJobParallelForTransform
        {
            [ReadOnly] public NativeArray<int> taParticle;
            [ReadOnly] public NativeArray<byte> write;
            [ReadOnly] public NativeArray<quaternion> outLocal;

            public void Execute(int index, TransformAccess t)
            {
                int p = taParticle[index];
                if (p < 0 || write[p] == 0 || !t.isValid) return;
                t.localRotation = outLocal[p];
            }
        }

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
        private struct RestJob : IJobParallelForTransform
        {
            [ReadOnly] public NativeArray<CharData> chars;
            [ReadOnly] public NativeArray<int> taChar, taParticle;
            [ReadOnly] public NativeArray<float3> restPos;
            [ReadOnly] public NativeArray<quaternion> restRot;

            public void Execute(int index, TransformAccess t)
            {
                int p = taParticle[index];
                if (p < 0 || chars[taChar[index]].rest == 0 || !t.isValid) return;
                t.SetLocalPositionAndRotation(restPos[p], restRot[p]);
            }
        }

        /// <summary>One character's swing: exactly the steps of the old per-character LateUpdate, in the same order.</summary>
        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
        private struct SimJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<CharData> chars;
            [ReadOnly] public NativeArray<float4x4> readM;
            [ReadOnly] public NativeArray<quaternion> readRot, readLocalRot;
            [ReadOnly] public NativeArray<float3> readLocalPos, readLocalScale;
            [ReadOnly] public NativeArray<int> parentG, taIndex, leafG, firstChildG, nextSiblingG;
            [ReadOnly] public NativeArray<float3> tipLocal, restPos;
            [ReadOnly] public NativeArray<float4> prof;
            [ReadOnly] public NativeArray<float> profRadius;
            [ReadOnly] public NativeArray<int> capTaA, capTaB;
            [ReadOnly] public NativeArray<float> capRadius, capExtend;
            [ReadOnly] public NativeArray<float> reach;
            [ReadOnly] public NativeArray<float3> side;

            // Each character owns its own ranges of these.
            [NativeDisableParallelForRestriction] public NativeArray<float3> pos, prev, target;
            [NativeDisableParallelForRestriction] public NativeArray<float4x4> curM;
            [NativeDisableParallelForRestriction] public NativeArray<quaternion> newRot, outLocal;
            [NativeDisableParallelForRestriction] public NativeArray<byte> write;
            [NativeDisableParallelForRestriction] public NativeArray<float3> segA, segB;
            [NativeDisableParallelForRestriction] public NativeArray<quaternion> capRot;

            public void Execute(int ci)
            {
                var c = chars[ci];
                if (c.mode == Mode.Skip) return;
                int s = c.pStart, n = c.pCount;
                for (int i = 0; i < n; i++) write[s + i] = 0;

                // Where the animation (or the bind pose) puts every particle this frame.
                for (int i = 0; i < n; i++)
                {
                    int g = s + i, ta = taIndex[g];
                    target[g] = ta >= 0 ? readM[ta].c3.xyz : math.mul(readM[taIndex[leafG[g]]], new float4(tipLocal[g], 1f)).xyz;
                }

                if (c.mode == Mode.Reset)
                {
                    for (int i = 0; i < n; i++) { pos[s + i] = target[s + i]; prev[s + i] = target[s + i]; }
                    return; // the bones already stand where the animation put them
                }

                float3 shift = float3.zero;
                if (c.mode == Mode.Simulate) Simulate(c);
                else shift = c.moved; // Hold
                Aim(c, shift);
            }

            private void Simulate(in CharData c)
            {
                int s = c.pStart, n = c.pCount, cs = c.capStart, cc = c.capCount;
                float scale = c.scale, dt = c.dt;
                float3 moved = c.moved;
                for (int k = 0; k < cc; k++)
                {
                    float3 p0 = readM[capTaA[cs + k]].c3.xyz, p1 = readM[capTaB[cs + k]].c3.xyz;
                    if (capExtend[cs + k] > 0f) p1 = p0 + Normalized(p0 - p1) * capExtend[cs + k] * scale;
                    segA[cs + k] = p0; segB[cs + k] = p1;
                    capRot[cs + k] = readRot[capTaA[cs + k]];
                }

                float frames = dt / Step60;
                float velScale = math.clamp(dt / math.max(1e-4f, c.prevDt), 0.25f, 4f);
                float3 gravity = c.gravity;
                for (int i = 0; i < n; i++)
                {
                    int g = s + i;
                    int pg = parentG[g];
                    if (pg < 0) { pos[g] = target[g]; prev[g] = target[g]; continue; }
                    float4 pr = prof[g]; // stiffness, damping, gravity, follow
                    float3 carry = moved * pr.w;
                    float3 velocity = (pos[g] - prev[g]) * (Pow(1f - pr.y, frames) * velScale);
                    prev[g] = pos[g] + carry;
                    float3 p = pos[g] + velocity + carry + gravity * pr.z;

                    float3 restVec = target[g] - target[pg];
                    float length = math.length(restVec);
                    float3 goal = pos[pg] + restVec;
                    p += (goal - p) * (1f - Pow(1f - pr.x, frames));

                    p = Keep(pos[pg], p, length);
                    float prad = profRadius[g] * scale;
                    int ri = c.reachStart + i * cc;
                    for (int k = 0; k < cc; k++)
                    {
                        float r = (capRadius[cs + k] * scale + prad) * reach[ri + k];
                        if (r <= 0f) continue;
                        float3 q = ClosestOnSegment(segA[cs + k], segB[cs + k], p);
                        float3 d = p - q;
                        float3 sideLocal = side[ri + k];
                        if (math.lengthsq(sideLocal) >= 1e-10f)
                        {
                            float3 nrm = math.mul(capRot[cs + k], sideLocal);
                            float along = math.dot(d, nrm);
                            float3 across = d - nrm * along;
                            if (along < 0f && math.lengthsq(across) < r * r * SideReach * SideReach)
                            {
                                p = q + across + nrm * r;
                                continue;
                            }
                        }
                        float m = math.lengthsq(d);
                        if (m >= r * r || m < 1e-10f) continue;
                        p = q + d * (r / math.sqrt(m));
                    }
                    pos[g] = p;
                }
            }

            /// <summary>Turns each bone, parents first, so it points at where its children swung (moved by <paramref name="shift"/>).</summary>
            private void Aim(in CharData c, float3 shift)
            {
                int s = c.pStart, n = c.pCount;
                for (int i = 0; i < n; i++)
                {
                    int g = s + i, ta = taIndex[g];
                    if (ta < 0) continue; // a tip
                    int pg = parentG[g];
                    float4x4 m;
                    quaternion rot;
                    if (pg >= 0)
                    {
                        // Below a bone turned earlier this frame: carried along by its turn.
                        m = math.mul(curM[pg], float4x4.TRS(readLocalPos[ta], readLocalRot[ta], readLocalScale[ta]));
                        rot = math.mul(newRot[pg], readLocalRot[ta]);
                    }
                    else { m = readM[ta]; rot = readRot[ta]; }
                    curM[g] = m; newRot[g] = rot;
                    if (firstChildG[g] < 0) continue;

                    float3 from = float3.zero, to = float3.zero;
                    float3 at = m.c3.xyz;
                    for (int ch = firstChildG[g]; ch >= 0; ch = nextSiblingG[ch])
                    {
                        float3 local = taIndex[ch] >= 0 ? restPos[ch] : tipLocal[ch];
                        from += math.mul(m, new float4(local, 1f)).xyz - at;
                        to += pos[ch] + shift - at;
                    }
                    if (math.lengthsq(from) < 1e-10f || math.lengthsq(to) < 1e-10f) continue;
                    quaternion q = FromTo(from, to);
                    quaternion turned = math.normalize(math.mul(q, rot));
                    var r3 = new float3x3(q);
                    curM[g] = new float4x4(
                        new float4(math.mul(r3, m.c0.xyz), m.c0.w), new float4(math.mul(r3, m.c1.xyz), m.c1.w),
                        new float4(math.mul(r3, m.c2.xyz), m.c2.w), m.c3);
                    newRot[g] = turned;
                    // The bone's parent's turn: the turned parent bone, or (a chain's first bone) the body bone it hangs on.
                    quaternion up = pg >= 0 ? newRot[pg] : math.mul(readRot[ta], math.inverse(readLocalRot[ta]));
                    outLocal[g] = math.normalize(math.mul(math.inverse(up), turned));
                    write[g] = 1;
                }
            }

            /// <summary>The shortest turn from <paramref name="from"/> to <paramref name="to"/> (as Quaternion.FromToRotation).</summary>
            private static quaternion FromTo(float3 from, float3 to)
            {
                float3 a = math.normalize(from), b = math.normalize(to);
                float d = math.dot(a, b);
                if (d <= -1f + 1e-6f)
                {
                    float3 axis = math.cross(new float3(1f, 0f, 0f), a);
                    if (math.lengthsq(axis) < 1e-6f) axis = math.cross(new float3(0f, 1f, 0f), a);
                    return quaternion.AxisAngle(math.normalize(axis), math.PI);
                }
                float3 cr = math.cross(a, b);
                float w = math.sqrt((1f + d) * 2f);
                return math.normalize(new quaternion(cr.x / w, cr.y / w, cr.z / w, w * 0.5f));
            }

            /// <summary>As Mathf.Pow, which works in double.</summary>
            private static float Pow(float x, float y) => (float)math.pow((double)x, (double)y);

            /// <summary>As Vector3.normalized: zero below 1e-5 m.</summary>
            private static float3 Normalized(float3 v)
            {
                float m = math.length(v);
                return m > 1e-5f ? v / m : float3.zero;
            }

            private static float3 Keep(float3 anchor, float3 p, float length)
            {
                float3 d = p - anchor;
                float m = math.length(d);
                return m > 1e-6f ? anchor + d * (length / m) : p;
            }

            private static float3 ClosestOnSegment(float3 a, float3 b, float3 p)
            {
                float3 ab = b - a;
                float len = math.lengthsq(ab);
                if (len < 1e-10f) return a;
                float t = math.saturate(math.dot(p - a, ab) / len);
                return a + ab * t;
            }
        }
    }

    /// <summary>Runs the shared <see cref="DangleBonesBatch"/> once a frame (Play only; tests step one character).</summary>
    [DefaultExecutionOrder(1000)] // after the animation, IK and the appearance's own LateUpdate merge
    [AddComponentMenu("")]
    internal sealed class DangleBonesRunner : MonoBehaviour
    {
        private static DangleBonesRunner instance;
        private static DangleBonesBatch batch;
        private static bool dirty;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() // Enter Play Mode without a domain reload
        {
            batch?.Dispose();
            batch = null;
            instance = null;
            dirty = false;
        }

        internal static void Register(DangleBones d)
        {
            if (batch == null) batch = new DangleBonesBatch(Allocator.Persistent);
            if (!batch.members.Contains(d)) batch.members.Add(d);
            dirty = true;
            if (instance == null)
            {
                var go = new GameObject("DangleBones (all)") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(go);
                instance = go.AddComponent<DangleBonesRunner>();
            }
        }

        internal static void Unregister(DangleBones d)
        {
            if (batch == null) return;
            if (batch.members.Remove(d)) dirty = true;
            d.batchIndex = -1;
        }

        /// <summary>The member's layout changed (a rebind): lay everyone out again before the next run.</summary>
        internal static void Changed(DangleBones d)
        {
            if (batch != null && batch.members.Contains(d)) { d.batchIndex = -1; dirty = true; }
        }

        private static void EnsureBuilt()
        {
            if (!dirty) return;
            dirty = false;
            batch.Keep();
            batch.Build();
        }

        private void Update()
        {
            if (batch == null || batch.Count == 0) return;
            EnsureBuilt();
            var chars = batch.chars;
            bool any = false;
            for (int i = 0; i < batch.members.Count; i++)
            {
                var c = chars[i];
                c.rest = batch.members[i].TakePosed() ? (byte)1 : (byte)0;
                any |= c.rest != 0;
                chars[i] = c;
            }
            if (any) batch.Rest();
        }

        private void LateUpdate()
        {
            if (batch == null || batch.Count == 0) return;
            EnsureBuilt();
            float dt = Time.deltaTime;
            var gravity = Physics.gravity;
            var chars = batch.chars;
            bool any = false;
            for (int i = 0; i < batch.members.Count; i++)
            {
                var c = chars[i];
                batch.members[i].PlanFrame(dt, gravity, ref c);
                any |= c.mode != DangleBonesBatch.Mode.Skip;
                chars[i] = c;
            }
            if (any) batch.Run();
        }

        private void OnDestroy()
        {
            if (instance != this) return;
            batch?.Dispose();
            batch = null;
            instance = null;
        }
    }
}
