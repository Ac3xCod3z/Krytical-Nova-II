using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace GI
{
    public abstract class UniversalPass : ScriptableRenderPass
    {
        public abstract void Release();

        public static void Fill(GISettings settings, ref PassData data, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();

            data.StereoMode = cameraData.xr.enabled;
            data.Camera = cameraData.camera;
            data.Atlas = GameObject.FindObjectOfType<SDFAtlas>();
            data.Settings = settings;
            // ColorRenderHandle / DepthRenderHandle are resolved from RenderGraph handles at execute time.
        }

        public static void ApplyRequirements(ScriptableRenderPass pass, int requirements)
        {
            if ((requirements & Requirements.Depth) == Requirements.Depth)
                pass.ConfigureInput(ScriptableRenderPassInput.Depth);

            if ((requirements & Requirements.GBuffer) == Requirements.GBuffer)
                pass.ConfigureInput(ScriptableRenderPassInput.Normal);

            if ((requirements & Requirements.Normals) == Requirements.Normals)
                pass.ConfigureInput(ScriptableRenderPassInput.Normal);

            if ((requirements & Requirements.Motion) == Requirements.Motion)
                pass.ConfigureInput(ScriptableRenderPassInput.Motion);
        }
    }

    public class UniversalPassWrapper : UniversalPass
    {
        private class RenderGraphPassData
        {
            public UniversalPassWrapper Wrapper;
            public PassData Data;
            public TextureHandle ColorTexture;
            public TextureHandle DepthTexture;
        }

        private Pass _pass;
        private GISettings _settings;
        private CommandExecutor _commandExecutor;

        public UniversalPassWrapper(GISettings settings, Pass pass)
        {
            _pass = pass;
            _settings = settings;
            _commandExecutor = new CommandExecutor();

            ApplyRequirements(this, pass.InputRequirements(settings));
        }

        public override void Release()
        {
            if (_pass != null)
            {
                _pass.Release();
                _pass = null;
            }
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddUnsafePass<RenderGraphPassData>(_pass.GetType().Name, out var passData))
            {
                var resourceData = frameData.Get<UniversalResourceData>();

                passData.Wrapper = this;
                UniversalPass.Fill(_settings, ref passData.Data, frameData);
                passData.ColorTexture = resourceData.activeColorTexture;
                passData.DepthTexture = resourceData.activeDepthTexture;

                if (passData.ColorTexture.IsValid())
                    builder.UseTexture(passData.ColorTexture, AccessFlags.ReadWrite);

                if (passData.DepthTexture.IsValid())
                    builder.UseTexture(passData.DepthTexture, AccessFlags.ReadWrite);

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (RenderGraphPassData data, UnsafeGraphContext context) =>
                {
                    data.Wrapper.ExecuteRenderGraph(data, context);
                });
            }
        }

        private void ExecuteRenderGraph(RenderGraphPassData passData, UnsafeGraphContext context)
        {
            // TextureHandle -> RTHandle conversion is only valid inside the render function.
            var data = passData.Data;
            data.ColorRenderHandle = passData.ColorTexture.IsValid() ? (RTHandle)passData.ColorTexture : null;
            data.DepthRenderHandle = passData.DepthTexture.IsValid() ? (RTHandle)passData.DepthTexture : null;

            _commandExecutor.Buffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            _pass.Execute(_commandExecutor, data);
        }
    }

    public class UniversalPassManager : PassManager
    {
        public GISettings Settings;
        public RenderPassEvent RenderEvent = RenderPassEvent.AfterRenderingGbuffer;
        public ScriptableRenderer ScriptableRenderer;

        private List<UniversalPass> _list = new List<UniversalPass>();

        protected override void ReleaseAdditional()
        {
            foreach (var pass in _list)
                pass.Release();

            _list.Clear();
        }

        public override void Run()
        {
            foreach (var pass in _list)
                ScriptableRenderer.EnqueuePass(pass);
        }

        public override void Add(Pass pass, Queue queue)
        {
            var wrapper = new UniversalPassWrapper(Settings, pass);

            switch (queue)
            {
                case Queue.BeforeRender: wrapper.renderPassEvent = RenderPassEvent.BeforeRendering; break;
                case Queue.Render: wrapper.renderPassEvent = RenderEvent; break;
                case Queue.BeforePostProcess: wrapper.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing; break;
                case Queue.AfterRender: wrapper.renderPassEvent = RenderPassEvent.AfterRendering; break;
            }

            _list.Add(wrapper);
        }
    }
}
