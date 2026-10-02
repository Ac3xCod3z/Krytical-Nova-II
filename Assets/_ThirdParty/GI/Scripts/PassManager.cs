using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    public abstract class PassManager
    {
        private List<Pass> _release = new List<Pass>();

        public abstract void Add(Pass pass, Queue queue);

        public abstract void Run();

        public void Release()
        {
            ReleaseAdditional();

            foreach (var pass in _release)
                pass.Release();

            _release.Clear();
        }

        protected void RegisterForRelease(Pass pass)
        {
            _release.Add(pass);
        }

        protected virtual void ReleaseAdditional()
        {
        }
    }

    public class BuiltinPassManager : PassManager
    {
        public PassData PassData;

        public CommandBuffer PreRender;
        public CommandBuffer Render;
        public CommandBuffer Apply;
        public CommandBuffer BeforePostProcess;
        public CommandBuffer AfterRender;

        private List<Pass> _pre_render = new List<Pass>();
        private List<Pass> _render = new List<Pass>();
        private List<Pass> _apply = new List<Pass>();
        private List<Pass> _before_post_process = new List<Pass>();
        private List<Pass> _after_render = new List<Pass>();

        private CommandExecutor _executor = new CommandExecutor();

        public override void Run()
        {
            _executor.Buffer = PreRender;

            foreach (var pass in _pre_render)
                _executor.Execute(pass, PassData);

            _executor.Buffer = Render;

            foreach (var pass in _render)
                _executor.Execute(pass, PassData);

            _executor.Buffer = Apply;

            foreach (var pass in _apply)
                _executor.Execute(pass, PassData);

            _executor.Buffer = BeforePostProcess;

            foreach (var pass in _before_post_process)
                _executor.Execute(pass, PassData);

            _executor.Buffer = AfterRender;

            foreach (var pass in _after_render)
                _executor.Execute(pass, PassData);
        }

        public override void Add(Pass pass, Queue queue)
        {
            RegisterForRelease(pass);

            switch (queue)
            {
                case Queue.BeforeRender: _pre_render.Add(pass); break;
                case Queue.Render: _render.Add(pass); break;
                case Queue.Apply: _apply.Add(pass); break;
                case Queue.BeforePostProcess: _before_post_process.Add(pass); break;
                case Queue.AfterRender: _after_render.Add(pass); break;
            }
        }
    }
}