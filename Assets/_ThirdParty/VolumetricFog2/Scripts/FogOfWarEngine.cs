//------------------------------------------------------------------------------------------------------------------
// Volumetric Fog & Mist 2
// Created by Kronnect
//------------------------------------------------------------------------------------------------------------------
using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace VolumetricFogAndMist2 {

    internal enum FowCommandType : byte {
        StampCircle,
        StampBounds,
        StampMeshBatch
    }

    internal struct FowCommand {
        public FowCommandType type;
        public Vector3 fowCenter, fowSize;
        public Vector3 position;
        public Vector3 extents;
        public float radius;
        public float newAlpha;
        public bool blendAlpha;
        public float duration, smoothness, restoreDelay, restoreDuration, restoreToAlpha;
        public float timestamp;
        public Vector2[] localTriangles;
        public int indicesLength;
        public Vector3[] positions;
        public float[] alphas;
    }

    internal struct FowTransition {
        public int pixelIndex;
        public float startTime, startDelay;
        public float duration;
        public int initialAlpha;
        public int targetAlpha;
        public int restoreToAlpha;
        public float restoreDelay;
        public float restoreDuration;
    }

    // Fog of war pixel state: stamp command queue, per-pixel transitions and a single persistent worker thread.
    // Every stamp is guaranteed to be applied (no fixed transition cap, no silent drops) and pending pixel
    // changes are always available for upload once per frame regardless of background activity.
    internal sealed class FogOfWarEngine {

        const long DRAIN_PIXEL_BUDGET = 65536;

        Color32[] buffer;
        int tw, th;
        int[] pixelTransition;
        FowTransition[] transitions = new FowTransition[4096];
        int transitionCount;
        readonly ConcurrentQueue<FowCommand> queue = new ConcurrentQueue<FowCommand>();
        readonly object sync = new object();
        readonly AutoResetEvent workSignal = new AutoResetEvent(false);
        Thread worker;
        volatile bool stopWorker;
        volatile bool isDirty;
        volatile bool mainThreadWaiting;
        float engineTime;

        public bool dirty => isDirty;
        public int activeTransitions => transitionCount;
        public bool hasPendingCommands => !queue.IsEmpty;

        public void Setup (Color32[] colorBuffer, int textureWidth, int textureHeight) {
            lock (sync) {
                buffer = colorBuffer;
                tw = textureWidth;
                th = textureHeight;
                int len = tw * th;
                if (pixelTransition == null || pixelTransition.Length != len) {
                    pixelTransition = new int[len];
                }
                for (int k = 0; k < len; k++) pixelTransition[k] = -1;
                transitionCount = 0;
                while (queue.TryDequeue(out _)) { }
            }
        }

        public void SetBuffer (Color32[] colorBuffer) {
            lock (sync) {
                buffer = colorBuffer;
            }
        }

        public void ClearTransitions () {
            lock (sync) {
                transitionCount = 0;
                if (pixelTransition != null) {
                    for (int k = 0; k < pixelTransition.Length; k++) pixelTransition[k] = -1;
                }
            }
        }

        public void Stop () {
            stopWorker = true;
            workSignal.Set();
            worker = null;
        }

        public void MarkUploaded () {
            isDirty = false;
        }

        #region Command dispatch

        public void Submit (in FowCommand cmd, bool synchronous) {
            queue.Enqueue(cmd);
            if (synchronous) {
                FlushQueue();
            } else {
                EnsureWorker();
            }
        }

        // drains all pending commands on the calling thread
        public void FlushQueue () {
            mainThreadWaiting = true;
            lock (sync) {
                mainThreadWaiting = false;
                DrainLocked(long.MaxValue);
            }
        }

        // called once per frame from the main thread
        public void Tick (float time, bool workerAllowed) {
            Volatile.Write(ref engineTime, time);
            if (worker == null || !workerAllowed) {
                mainThreadWaiting = true;
                lock (sync) {
                    mainThreadWaiting = false;
                    DrainLocked(long.MaxValue);
                    StepTransitionsLocked(time);
                }
            } else {
                // keep transitions frame-accurate when the worker is not holding the lock
                if (Monitor.TryEnter(sync)) {
                    try {
                        StepTransitionsLocked(time);
                    } finally {
                        Monitor.Exit(sync);
                    }
                }
                workSignal.Set();
            }
        }

        // the worker yields between chunks while a main-thread caller waits, so these acquisitions stay short
        public void EnterSyncPriority () {
            mainThreadWaiting = true;
            Monitor.Enter(sync);
            mainThreadWaiting = false;
        }

        public void ExitSyncPriority () {
            Monitor.Exit(sync);
        }

        void EnsureWorker () {
            if (worker != null) return;
            stopWorker = false;
            worker = new Thread(WorkerLoop) {
                IsBackground = true,
                Name = "VF2 FogOfWar"
            };
            worker.Start();
        }

        void WorkerLoop () {
            while (!stopWorker && ReferenceEquals(worker, Thread.CurrentThread)) {
                workSignal.WaitOne(50);
                if (stopWorker || !ReferenceEquals(worker, Thread.CurrentThread)) return;
                try {
                    bool more = true;
                    while (more && !stopWorker) {
                        while (mainThreadWaiting) Thread.Yield();
                        lock (sync) {
                            more = DrainLocked(DRAIN_PIXEL_BUDGET);
                        }
                    }
                    while (mainThreadWaiting) Thread.Yield();
                    lock (sync) {
                        StepTransitionsLocked(Volatile.Read(ref engineTime));
                    }
                } catch (Exception ex) {
                    Debug.LogError("Fog of war worker: " + ex.Message);
                }
            }
        }

        // returns true if commands remain queued; budget bounds lock hold time by stamped pixel area
        bool DrainLocked (long pixelBudget) {
            long spent = 0;
            while (spent < pixelBudget && queue.TryDequeue(out FowCommand cmd)) {
                Execute(ref cmd);
                spent += EstimateCost(ref cmd);
            }
            return !queue.IsEmpty;
        }

        long EstimateCost (ref FowCommand cmd) {
            switch (cmd.type) {
                case FowCommandType.StampCircle: {
                        long delta = (long)(th * (cmd.radius / cmd.fowSize.z));
                        return (2 * delta + 1) * (2 * delta + 1);
                    }
                case FowCommandType.StampBounds: {
                        long dx = (long)(tw * (cmd.extents.x / cmd.fowSize.x));
                        long dz = (long)(th * (cmd.extents.z / cmd.fowSize.z));
                        return (2 * dx + 1) * (2 * dz + 1);
                    }
                case FowCommandType.StampMeshBatch: {
                        long dx = (long)(tw * (cmd.extents.x / cmd.fowSize.x));
                        long dz = (long)(th * (cmd.extents.z / cmd.fowSize.z));
                        long positions = cmd.positions != null ? cmd.positions.Length : 1;
                        return positions * (2 * dx + 1) * (2 * dz + 1);
                    }
            }
            return 1;
        }

        void Execute (ref FowCommand cmd) {
            if (buffer == null || buffer.Length == 0) return;
            switch (cmd.type) {
                case FowCommandType.StampCircle: StampCircle(ref cmd); break;
                case FowCommandType.StampBounds: StampBounds(ref cmd); break;
                case FowCommandType.StampMeshBatch: StampMeshBatch(ref cmd); break;
            }
        }

        #endregion

        #region Transitions

        void AddTransition (int pixelIndex, int initialAlpha, int targetAlpha, float delay, float duration, int restoreToAlpha, float restoreDelay, float restoreDuration, float now) {
            int index = pixelTransition[pixelIndex];
            if (index >= 0) {
                if (transitions[index].targetAlpha <= targetAlpha && transitions[index].restoreToAlpha == restoreToAlpha && transitions[index].restoreDelay == restoreDelay && transitions[index].restoreDuration == restoreDuration) {
                    return;
                }
            } else {
                if (transitionCount >= transitions.Length) {
                    Array.Resize(ref transitions, transitions.Length * 2);
                }
                index = transitionCount++;
                pixelTransition[pixelIndex] = index;
            }
            transitions[index].pixelIndex = pixelIndex;
            transitions[index].startTime = now;
            transitions[index].startDelay = delay;
            transitions[index].duration = duration;
            transitions[index].initialAlpha = initialAlpha;
            transitions[index].targetAlpha = targetAlpha;
            transitions[index].restoreToAlpha = restoreToAlpha;
            transitions[index].restoreDelay = restoreDelay;
            transitions[index].restoreDuration = restoreDuration;
        }

        void StepTransitionsLocked (float now) {
            for (int k = 0; k < transitionCount; k++) {
                float elapsed = now - transitions[k].startTime - transitions[k].startDelay;
                if (elapsed <= 0) continue;
                float duration = transitions[k].duration;
                float t = duration <= 0 ? 1 : elapsed / duration;
                if (t < 0) t = 0; else if (t > 1f) t = 1f;
                int alpha = (int)(transitions[k].initialAlpha + (transitions[k].targetAlpha - transitions[k].initialAlpha) * t);
                buffer[transitions[k].pixelIndex].a = (byte)alpha;
                isDirty = true;
                if (t >= 1f) {
                    if (transitions[k].targetAlpha != transitions[k].restoreToAlpha && transitions[k].restoreDelay > 0) {
                        transitions[k].duration = transitions[k].restoreDuration;
                        transitions[k].startTime = now;
                        transitions[k].startDelay = transitions[k].restoreDelay;
                        transitions[k].initialAlpha = transitions[k].targetAlpha;
                        transitions[k].targetAlpha = transitions[k].restoreToAlpha;
                        transitions[k].restoreDelay = 0;
                        transitions[k].restoreDuration = 0;
                    } else {
                        RemoveTransitionAt(k);
                        k--;
                    }
                }
            }
        }

        void RemoveTransitionAt (int k) {
            pixelTransition[transitions[k].pixelIndex] = -1;
            int last = --transitionCount;
            if (k != last) {
                transitions[k] = transitions[last];
                pixelTransition[transitions[k].pixelIndex] = k;
            }
        }

        #endregion

        #region Rasterizers

        void StampCircle (ref FowCommand cmd) {
            float tx = (cmd.position.x - cmd.fowCenter.x) / cmd.fowSize.x + 0.5f;
            if (tx < 0 || tx > 1f) return;
            float tz = (cmd.position.z - cmd.fowCenter.z) / cmd.fowSize.z + 0.5f;
            if (tz < 0 || tz > 1f) return;

            int px = (int)(tx * tw);
            int pz = (int)(tz * th);
            float sm = 0.0001f + cmd.smoothness;
            byte newAlpha8 = (byte)(cmd.newAlpha * 255);
            float tr = cmd.radius / cmd.fowSize.z;
            int delta = (int)(th * tr);
            int deltaSqr = delta * delta;
            byte restoreAlpha = (byte)(255 * cmd.restoreToAlpha);
            float now = cmd.timestamp;

            int minR = Mathf.Max(0, pz - delta);
            int maxR = Mathf.Min(th - 1, pz + delta);
            int minC = Mathf.Max(0, px - delta);
            int maxC = Mathf.Min(tw - 1, px + delta);

            for (int r = minR; r <= maxR; r++) {
                int rOffset = r * tw;
                int rDistSqr = (pz - r) * (pz - r);
                for (int c = minC; c <= maxC; c++) {
                    int cDistSqr = (px - c) * (px - c);
                    int distanceSqr = rDistSqr + cDistSqr;
                    if (distanceSqr <= deltaSqr) {
                        int colorBufferPos = rOffset + c;
                        Color32 colorBuffer = buffer[colorBufferPos];
                        if (!cmd.blendAlpha) {
                            colorBuffer.a = 255;
                        }
                        distanceSqr = deltaSqr - distanceSqr;
                        float t = (float)distanceSqr / (deltaSqr * sm);
                        t = 1f - t;
                        if (t < 0) {
                            t = 0;
                        } else if (t > 1f) {
                            t = 1f;
                        }
                        byte targetAlpha = (byte)(newAlpha8 + (colorBuffer.a - newAlpha8) * t);
                        if (colorBuffer.a != targetAlpha || cmd.restoreDelay > 0) {
                            if (cmd.duration > 0) {
                                AddTransition(colorBufferPos, colorBuffer.a, targetAlpha, 0, cmd.duration, restoreAlpha, cmd.restoreDelay, cmd.restoreDuration, now);
                            } else {
                                colorBuffer.a = targetAlpha;
                                buffer[colorBufferPos] = colorBuffer;
                                isDirty = true;
                                if (cmd.restoreDelay > 0) {
                                    AddTransition(colorBufferPos, targetAlpha, restoreAlpha, cmd.restoreDelay, cmd.restoreDuration, targetAlpha, 0, 0, now);
                                }
                            }
                        }
                    }
                }
            }
        }

        void StampBounds (ref FowCommand cmd) {
            float tx = (cmd.position.x - cmd.fowCenter.x) / cmd.fowSize.x + 0.5f;
            if (tx < 0 || tx > 1f) return;
            float tz = (cmd.position.z - cmd.fowCenter.z) / cmd.fowSize.z + 0.5f;
            if (tz < 0 || tz > 1f) return;

            int px = (int)(tx * tw);
            int pz = (int)(tz * th);
            byte newAlpha8 = (byte)(cmd.newAlpha * 255);
            float trz = cmd.extents.z / cmd.fowSize.z;
            float trx = cmd.extents.x / cmd.fowSize.x;
            float aspect1 = trx > trz ? 1f : trz / trx;
            float aspect2 = trx > trz ? trx / trz : 1f;
            int deltaz = (int)(th * trz);
            int deltazSqr = deltaz * deltaz;
            int deltax = (int)(tw * trx);
            int deltaxSqr = deltax * deltax;
            float sm = 0.0001f + cmd.smoothness;
            byte restoreAlpha = (byte)(cmd.restoreToAlpha * 255);
            float now = cmd.timestamp;

            int minR = Mathf.Max(0, pz - deltaz);
            int maxR = Mathf.Min(th - 1, pz + deltaz);
            int minC = Mathf.Max(0, px - deltax);
            int maxC = Mathf.Min(tw - 1, px + deltax);

            for (int r = minR; r <= maxR; r++) {
                int rOffset = r * tw;
                int distancezSqr = (pz - r) * (pz - r);
                distancezSqr = deltazSqr - distancezSqr;
                float t1 = (float)distancezSqr * aspect1 / (deltazSqr * sm);

                for (int c = minC; c <= maxC; c++) {
                    int distancexSqr = (px - c) * (px - c);
                    int colorBufferPos = rOffset + c;
                    Color32 colorBuffer = buffer[colorBufferPos];
                    if (!cmd.blendAlpha) colorBuffer.a = 255;

                    distancexSqr = deltaxSqr - distancexSqr;
                    float t2 = (float)distancexSqr * aspect2 / (deltaxSqr * sm);
                    float t = t1 < t2 ? t1 : t2;
                    t = 1f - t;
                    if (t < 0) t = 0; else if (t > 1f) t = 1f;
                    byte targetAlpha = (byte)(newAlpha8 + (colorBuffer.a - newAlpha8) * t);
                    if (colorBuffer.a != targetAlpha || cmd.restoreDelay > 0) {
                        if (cmd.duration > 0) {
                            AddTransition(colorBufferPos, colorBuffer.a, targetAlpha, 0, cmd.duration, restoreAlpha, cmd.restoreDelay, cmd.restoreDuration, now);
                        } else {
                            colorBuffer.a = targetAlpha;
                            buffer[colorBufferPos] = colorBuffer;
                            isDirty = true;
                            if (cmd.restoreDelay > 0) {
                                AddTransition(colorBufferPos, targetAlpha, restoreAlpha, cmd.restoreDelay, cmd.restoreDuration, restoreAlpha, 0, 0, now);
                            }
                        }
                    }
                }
            }
        }

        void StampMeshBatch (ref FowCommand cmd) {
            Vector2[] localTriangles = cmd.localTriangles;
            int indicesLength = cmd.indicesLength;
            Vector3[] positions = cmd.positions;
            float[] alphas = cmd.alphas;
            float trz = cmd.extents.z / cmd.fowSize.z;
            float trx = cmd.extents.x / cmd.fowSize.x;
            int deltaz = (int)(th * trz);
            int deltax = (int)(tw * trx);
            byte restoreAlpha = (byte)(cmd.restoreToAlpha * 255);
            float now = cmd.timestamp;

            for (int p = 0; p < positions.Length; p++) {
                Vector3 worldPosition = positions[p];

                float tx = (worldPosition.x - cmd.fowCenter.x) / cmd.fowSize.x + 0.5f;
                if (tx < 0 || tx > 1f) continue;
                float tz = (worldPosition.z - cmd.fowCenter.z) / cmd.fowSize.z + 0.5f;
                if (tz < 0 || tz > 1f) continue;

                byte newAlpha8 = (byte)(alphas[p] * 255);

                int px = (int)(tx * tw);
                int pz = (int)(tz * th);
                int r0 = pz - deltaz;
                if (r0 < 0) r0 = 0; else if (r0 >= th) r0 = th - 1;
                int r1 = pz + deltaz;
                if (r1 < 0) r1 = 0; else if (r1 >= th) r1 = th - 1;
                int c0 = px - deltax;
                if (c0 < 0) c0 = 0; else if (c0 >= tw) c0 = tw - 1;
                int c1 = px + deltax;
                if (c1 < 0) c1 = 0; else if (c1 >= tw) c1 = tw - 1;

                int index = 0;
                Vector2 v0 = localTriangles[index];
                Vector2 v1 = localTriangles[index + 1];
                Vector2 v2 = localTriangles[index + 2];

                for (int r = r0; r <= r1; r++) {
                    int rr = r * tw;
                    float wz = (((r + 0.5f) / th) - 0.5f) * cmd.fowSize.z + cmd.fowCenter.z - worldPosition.z;
                    for (int c = c0; c <= c1; c++) {
                        float wx = (((c + 0.5f) / tw) - 0.5f) * cmd.fowSize.x + cmd.fowCenter.x - worldPosition.x;
                        for (int i = 0; i < indicesLength; i += 3) {
                            if (PointInTriangle(wx, wz, v0.x, v0.y, v1.x, v1.y, v2.x, v2.y)) {
                                int colorBufferPos = rr + c;
                                Color32 colorBuffer = buffer[colorBufferPos];
                                if (colorBuffer.a != newAlpha8 || cmd.restoreDelay > 0) {
                                    if (cmd.duration > 0) {
                                        AddTransition(colorBufferPos, colorBuffer.a, newAlpha8, 0, cmd.duration, restoreAlpha, cmd.restoreDelay, cmd.restoreDuration, now);
                                    } else {
                                        colorBuffer.a = newAlpha8;
                                        buffer[colorBufferPos] = colorBuffer;
                                        isDirty = true;
                                        if (cmd.restoreDelay > 0) {
                                            AddTransition(colorBufferPos, newAlpha8, restoreAlpha, cmd.restoreDelay, cmd.restoreDuration, restoreAlpha, 0, 0, now);
                                        }
                                    }
                                }
                                break;
                            } else {
                                index += 3;
                                index %= indicesLength;
                                v0 = localTriangles[index];
                                v1 = localTriangles[index + 1];
                                v2 = localTriangles[index + 2];
                            }
                        }
                    }
                }
            }
        }

        // collider shapes need Collider.ClosestPoint so they run synchronously on the main thread
        public void StampColliderSync (ref FowCommand cmd, Collider collider, Bounds bounds) {
            mainThreadWaiting = true;
            lock (sync) {
                mainThreadWaiting = false;
                DrainLocked(long.MaxValue);

                float tx = (cmd.position.x - cmd.fowCenter.x) / cmd.fowSize.x + 0.5f;
                if (tx < 0 || tx > 1f) return;
                float tz = (cmd.position.z - cmd.fowCenter.z) / cmd.fowSize.z + 0.5f;
                if (tz < 0 || tz > 1f) return;

                int px = (int)(tx * tw);
                int pz = (int)(tz * th);
                byte newAlpha8 = (byte)(cmd.newAlpha * 255);
                float trz = cmd.extents.z / cmd.fowSize.z;
                float trx = cmd.extents.x / cmd.fowSize.x;
                float aspect1 = trx > trz ? 1f : trz / trx;
                float aspect2 = trx > trz ? trx / trz : 1f;
                int deltaz = (int)(th * trz);
                int deltazSqr = deltaz * deltaz;
                int deltax = (int)(tw * trx);
                int deltaxSqr = deltax * deltax;
                float sm = 0.0001f + cmd.smoothness;
                byte restoreAlpha = (byte)(cmd.restoreToAlpha * 255);
                float now = cmd.timestamp;

                Vector3 wpos = bounds.min;
                wpos.y = bounds.center.y;
                for (int rr = 0; rr <= deltaz * 2; rr++) {
                    int r = pz - deltaz + rr;
                    if (r >= 0 && r < th) {
                        int distancezSqr = (pz - r) * (pz - r);
                        distancezSqr = deltazSqr - distancezSqr;
                        float t1 = (float)distancezSqr * aspect1 / (deltazSqr * sm);
                        wpos.z = bounds.min.z + bounds.size.z * rr / (deltaz * 2f);
                        for (int cc = 0; cc <= deltax * 2; cc++) {
                            int c = px - deltax + cc;
                            if (c >= 0 && c < tw) {
                                wpos.x = bounds.min.x + bounds.size.x * cc / (deltax * 2f);
                                Vector3 colliderPos = collider.ClosestPoint(wpos);
                                if (colliderPos != wpos) continue;

                                int distancexSqr = (px - c) * (px - c);
                                int colorBufferPos = r * tw + c;
                                Color32 colorBuffer = buffer[colorBufferPos];
                                if (!cmd.blendAlpha) colorBuffer.a = 255;
                                distancexSqr = deltaxSqr - distancexSqr;
                                float t2 = (float)distancexSqr * aspect2 / (deltaxSqr * sm);
                                float t = t1 < t2 ? t1 : t2;
                                t = 1f - t;
                                if (t < 0) t = 0; else if (t > 1f) t = 1f;
                                byte targetAlpha = (byte)(newAlpha8 + (colorBuffer.a - newAlpha8) * t);
                                if (colorBuffer.a != targetAlpha || cmd.restoreDelay > 0) {
                                    if (cmd.duration > 0) {
                                        AddTransition(colorBufferPos, colorBuffer.a, targetAlpha, 0, cmd.duration, restoreAlpha, cmd.restoreDelay, cmd.restoreDuration, now);
                                    } else {
                                        colorBuffer.a = targetAlpha;
                                        buffer[colorBufferPos] = colorBuffer;
                                        isDirty = true;
                                        if (cmd.restoreDelay > 0) {
                                            AddTransition(colorBufferPos, targetAlpha, restoreAlpha, cmd.restoreDelay, cmd.restoreDuration, restoreAlpha, 0, 0, now);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        public void ResetColliderSync (Collider collider, Bounds bounds, Vector3 fowCenter, Vector3 fowSize, float alpha) {
            mainThreadWaiting = true;
            lock (sync) {
                mainThreadWaiting = false;
                DrainLocked(long.MaxValue);

                Vector3 worldPosition = bounds.center;
                float tx = (worldPosition.x - fowCenter.x) / fowSize.x + 0.5f;
                if (tx < 0 || tx > 1f) return;
                float tz = (worldPosition.z - fowCenter.z) / fowSize.z + 0.5f;
                if (tz < 0 || tz > 1f) return;

                int px = (int)(tx * tw);
                int pz = (int)(tz * th);
                float trz = bounds.extents.z / fowSize.z;
                float trx = bounds.extents.x / fowSize.x;
                int deltaz = (int)(th * trz);
                int deltax = (int)(tw * trx);
                byte fogAlpha = (byte)(alpha * 255);

                Vector3 wpos = bounds.min;
                wpos.y = bounds.center.y;
                for (int rr = 0; rr <= deltaz * 2; rr++) {
                    int r = pz - deltaz + rr;
                    if (r >= 0 && r < th) {
                        int rOffset = r * tw;
                        wpos.z = bounds.min.z + bounds.size.z * rr / (deltaz * 2f);
                        for (int cc = 0; cc <= deltax * 2; cc++) {
                            int c = px - deltax + cc;
                            if (c >= 0 && c < tw) {
                                wpos.x = bounds.min.x + bounds.size.x * cc / (deltax * 2f);
                                Vector3 colliderPos = collider.ClosestPoint(wpos);
                                if (colliderPos != wpos) continue;
                                int colorBufferPos = rOffset + c;
                                Color32 colorBuffer = buffer[colorBufferPos];
                                colorBuffer.a = fogAlpha;
                                buffer[colorBufferPos] = colorBuffer;
                                isDirty = true;
                            }
                        }
                    }
                }
            }
        }

        public void ResetCircleSync (Vector3 position, float radius, Vector3 fowCenter, Vector3 fowSize, float alpha) {
            mainThreadWaiting = true;
            lock (sync) {
                mainThreadWaiting = false;
                DrainLocked(long.MaxValue);

                float tx = (position.x - fowCenter.x) / fowSize.x + 0.5f;
                if (tx < 0 || tx > 1f) return;
                float tz = (position.z - fowCenter.z) / fowSize.z + 0.5f;
                if (tz < 0 || tz > 1f) return;

                int px = (int)(tx * tw);
                int pz = (int)(tz * th);
                float tr = radius / fowSize.z;
                int delta = (int)(th * tr);
                int deltaSqr = delta * delta;
                byte fogAlpha = (byte)(alpha * 255);
                for (int r = pz - delta; r <= pz + delta; r++) {
                    if (r >= 0 && r < th) {
                        for (int c = px - delta; c <= px + delta; c++) {
                            if (c >= 0 && c < tw) {
                                int distanceSqr = (pz - r) * (pz - r) + (px - c) * (px - c);
                                if (distanceSqr <= deltaSqr) {
                                    int colorBufferPos = r * tw + c;
                                    Color32 colorBuffer = buffer[colorBufferPos];
                                    colorBuffer.a = fogAlpha;
                                    buffer[colorBufferPos] = colorBuffer;
                                    isDirty = true;
                                }
                            }
                        }
                    }
                }
            }
        }

        public void ResetRectSync (Vector3 position, float extentsX, float extentsZ, Vector3 fowCenter, Vector3 fowSize, float alpha) {
            mainThreadWaiting = true;
            lock (sync) {
                mainThreadWaiting = false;
                DrainLocked(long.MaxValue);

                float tx = (position.x - fowCenter.x) / fowSize.x + 0.5f;
                if (tx < 0 || tx > 1f) return;
                float tz = (position.z - fowCenter.z) / fowSize.z + 0.5f;
                if (tz < 0 || tz > 1f) return;

                int px = (int)(tx * tw);
                int pz = (int)(tz * th);
                float trz = extentsZ / fowSize.z;
                float trx = extentsX / fowSize.x;
                int deltaz = (int)(th * trz);
                int deltax = (int)(tw * trx);
                byte fogAlpha = (byte)(alpha * 255);
                for (int r = pz - deltaz; r <= pz + deltaz; r++) {
                    if (r >= 0 && r < th) {
                        for (int c = px - deltax; c <= px + deltax; c++) {
                            if (c >= 0 && c < tw) {
                                int colorBufferPos = r * tw + c;
                                Color32 colorBuffer = buffer[colorBufferPos];
                                colorBuffer.a = fogAlpha;
                                buffer[colorBufferPos] = colorBuffer;
                                isDirty = true;
                            }
                        }
                    }
                }
            }
        }

        static float Sign (float p1x, float p1z, float p2x, float p2z, float p3x, float p3z) {
            return (p1x - p3x) * (p2z - p3z) - (p2x - p3x) * (p1z - p3z);
        }

        static bool PointInTriangle (float x, float z, float v1x, float v1z, float v2x, float v2z, float v3x, float v3z) {
            float d1 = Sign(x, z, v1x, v1z, v2x, v2z);
            float d2 = Sign(x, z, v2x, v2z, v3x, v3z);
            float d3 = Sign(x, z, v3x, v3z, v1x, v1z);

            bool has_neg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            bool has_pos = (d1 > 0) || (d2 > 0) || (d3 > 0);

            return !(has_neg && has_pos);
        }

        #endregion
    }
}
