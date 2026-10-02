using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    public enum Queue
    {
        BeforeRender,
        Render,
        Apply,
        BeforePostProcess,
        AfterRender
    }

    public static class Requirements
    {
        public static int Depth = 0x1;
        public static int Normals = 0x2;
        public static int Motion = 0x4;
        public static int GBuffer = 0x8 | Depth;
    }

    public struct PassData
    {
        public bool DeferredMode;
        public bool StereoMode;
        public GISettings Settings;
        public Camera Camera;
        public SDFAtlas Atlas;
        public RTHandle ColorRenderHandle;
        public RTHandle DepthRenderHandle;
    }

    public abstract class PassScope
    {
        public abstract CommandBuffer Begin(string name);
        public abstract void End();
    }

    public abstract class Pass
    {
        public virtual void Release()
        {
        }

        public abstract void Execute(Executor executor, PassData data);

        public virtual int InputRequirements(GISettings settings)
        {
            return 0;
        }
    }

    public class PassList : Pass
    {
        public List<Pass> Passes = new List<Pass>();

        public override void Release()
        {
            foreach (var pass in Passes)
                pass.Release();

            Passes.Clear();
        }

        public override int InputRequirements(GISettings settings)
        {
            int result = 0;

            foreach (var pass in Passes)
                result |= pass.InputRequirements(settings);

            return result;
        }

        public override void Execute(Executor executor, PassData data)
        {
            foreach (var pass in Passes)
                pass.Execute(executor, data);
        }
    }
}