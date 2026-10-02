using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class SDFGridUpdatePass : Pass
    {
        private SDFGrid _sdf_grid;

        private Kernel _karguments;
        private Kernel _kcull;
        private Kernel _kwrite;
        private Kernel _kwrite_clear;
        private Kernel _koffset;

        public SDFGridUpdatePass(SDFGrid sdf_grid)
        {
            _sdf_grid = sdf_grid;

            _karguments = new Kernel(Resources.Load<ComputeShader>("Shaders/SDF/CullArguments"), "Main");
            _kcull = new Kernel(Resources.Load<ComputeShader>("Shaders/SDF/Cull"), "Main");
            _koffset = new Kernel(Resources.Load<ComputeShader>("Shaders/SDF/CullOffset"), "Main");
            _kwrite = new Kernel(Resources.Load<ComputeShader>("Shaders/SDF/CullWrite"), "Main");
            _kwrite_clear = new Kernel(Resources.Load<ComputeShader>("Shaders/SDF/CullClear"), "Main");
        }

        public override void Execute(Executor executor, PassData data)
        {
            var pass = executor.Pass();
            var cmd = pass.Begin("SDF Grid Update");

            //

            _sdf_grid.Parameters(cmd, _kwrite_clear);
            _kwrite_clear.DispatchEnoughFor(cmd, Defines.GDF_GRID_DIM * Defines.GDF_GRID_DIM * Defines.GDF_GRID_DIM);

            //

            _karguments.Bind(cmd, P.Requests, data.Atlas.Requests);
            _karguments.Bind(cmd, P.ArgumentsRW, _sdf_grid.Arguments);
            _karguments.DispatchOne(cmd);

            //

            _sdf_grid.Parameters(cmd, _kcull);

            _kcull.Setf(cmd, P.Influence, 4);
            _kcull.Seti(cmd, P.DFInstanceCount, data.Atlas.Cursor);

            _kcull.Bind(cmd, P.DFInstances, data.Atlas.Buffer);
            _kcull.Bind(cmd, P.SDFAssets, data.Atlas.Assets);

            _kcull.Bind(cmd, P.Requests, data.Atlas.Requests);

            _kcull.Dispatch(cmd, _sdf_grid.Arguments);

            //

            _sdf_grid.Parameters(cmd, _koffset);
            _koffset.DispatchEnoughFor(cmd, Defines.GDF_GRID_DIM * Defines.GDF_GRID_DIM * Defines.GDF_GRID_DIM);

            //

            _sdf_grid.Parameters(cmd, _kwrite);

            _kwrite.Bind(cmd, P.Requests, data.Atlas.Requests);

            _kwrite.Dispatch(cmd, _sdf_grid.Arguments);

            //

            pass.End();
        }
    }
}