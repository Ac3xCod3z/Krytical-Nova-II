using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    public abstract class Executor
    {
        public abstract bool IsScriptable();

        public abstract PassScope Pass();

        public abstract void ExecuteCommandBuffer(CommandBuffer cmd);

        public void Execute(Pass pass, PassData data)
        {
            pass.Execute(this, data);
        }
    }

    public class ScriptableExecutor : Executor
    {
        public class ScriptableScope : PassScope
        {
            private CommandBuffer _cmd;
            private ScriptableExecutor _executor;

            public ScriptableScope(ScriptableExecutor executor)
            {
                _executor = executor;
            }

            public override CommandBuffer Begin(string name)
            {
                if (_cmd != null)
                    CommandBufferPool.Release(_cmd);

                _cmd = CommandBufferPool.Get(name);
                _cmd.Clear();

                return _cmd;
            }

            public override void End()
            {
                if (_cmd == null)
                    return;

                _executor.Context.ExecuteCommandBuffer(_cmd);
                CommandBufferPool.Release(_cmd);
                _cmd = null;

                _executor.Return(this);
            }
        }

        public ScriptableRenderContext Context;

        private List<ScriptableScope> _scopes = new List<ScriptableScope>();

        public override bool IsScriptable()
        {
            return true;
        }

        public override PassScope Pass()
        {
            if (_scopes.Count == 0)
                _scopes.Add(new ScriptableScope(this));

            var scope = _scopes[_scopes.Count - 1];
            _scopes.RemoveAt(_scopes.Count - 1);

            return scope;
        }

        public void Return(ScriptableScope scope)
        {
            _scopes.Add(scope);
        }

        public override void ExecuteCommandBuffer(CommandBuffer cmd)
        {
            Context.ExecuteCommandBuffer(cmd);
        }
    }

    public class GraphicsExecutor : Executor
    {
        public class GraphicsScope : PassScope
        {
            private CommandBuffer _cmd;
            private GraphicsExecutor _executor;

            public GraphicsScope(GraphicsExecutor executor)
            {
                _executor = executor;
            }

            public override CommandBuffer Begin(string name)
            {
                if (_cmd != null)
                    CommandBufferPool.Release(_cmd);

                _cmd = CommandBufferPool.Get(name);
                _cmd.Clear();

                return _cmd;
            }

            public override void End()
            {
                if (_cmd == null)
                    return;

                Graphics.ExecuteCommandBuffer(_cmd);
                CommandBufferPool.Release(_cmd);
                _cmd = null;

                _executor.Return(this);
            }
        }

        private List<GraphicsScope> _scopes = new List<GraphicsScope>();

        public override bool IsScriptable()
        {
            return true;
        }

        public override PassScope Pass()
        {
            if (_scopes.Count == 0)
                _scopes.Add(new GraphicsScope(this));

            var scope = _scopes[_scopes.Count - 1];
            _scopes.RemoveAt(_scopes.Count - 1);

            return scope;
        }

        public void Return(GraphicsScope scope)
        {
            _scopes.Add(scope);
        }

        public override void ExecuteCommandBuffer(CommandBuffer cmd)
        {
            Graphics.ExecuteCommandBuffer(cmd);
        }
    }

    public class CommandExecutor : Executor
    {
        public class CommandScope : PassScope
        {
            private CommandExecutor _executor;
            private string _name;

            public CommandScope(CommandExecutor executor)
            {
                _executor = executor;
            }

            public override CommandBuffer Begin(string name)
            {
                _name = name;
                _executor.Buffer.BeginSample(name);

                return _executor.Buffer;
            }

            public override void End()
            {
                _executor.Buffer.EndSample(_name);
                _executor.Return(this);
            }
        }

        public CommandBuffer Buffer;

        private List<CommandScope> _scopes = new List<CommandScope>();

        public override bool IsScriptable()
        {
            return true;
        }

        public override PassScope Pass()
        {
            if (_scopes.Count == 0)
                _scopes.Add(new CommandScope(this));

            var scope = _scopes[_scopes.Count - 1];
            _scopes.RemoveAt(_scopes.Count - 1);

            return scope;
        }

        public void Return(CommandScope scope)
        {
            _scopes.Add(scope);
        }

        public override void ExecuteCommandBuffer(CommandBuffer cmd)
        {
            Graphics.ExecuteCommandBuffer(cmd);
        }
    }
}
