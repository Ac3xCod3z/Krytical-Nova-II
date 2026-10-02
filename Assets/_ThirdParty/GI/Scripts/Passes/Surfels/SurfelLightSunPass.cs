using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class SurfelLightSunPass : Pass
    {
        private Kernel _kernel;

        private GDF _gdf;
        private Lights _lights;
        private Probes _probes;
        private Surfels _surfels;

        public SurfelLightSunPass(GDF gdf, Probes probes, Surfels surfels, Lights lights)
        {
            _gdf = gdf;
            _probes = probes;
            _surfels = surfels;
            _lights = lights;

            _kernel = new Kernel(Resources.Load<ComputeShader>("Shaders/Surfels/LightSun"), "Main");
        }

        public override void Execute(Executor executor, PassData data)
        {
            if (_lights.Directional.Count > 0)
            {
                var camera = data.Camera;
                var reference_position = camera.transform.position;
                var gdf_base_position = Util.GridCoordToPosition(Util.GridOriginCoord(reference_position, Defines.GDF_CELL_DIM, Defines.GDF_GRID_DIM), Defines.GDF_CELL_DIM);

                var pass = executor.Pass();
                var cmd = pass.Begin("Surfel Light Sun");

                _probes.Parameters(cmd, _kernel);
                _surfels.Parameters(cmd, _kernel);
                _gdf.Parameters(cmd, _kernel);
                _gdf.Textures(cmd, _kernel);

                _kernel.Keyword("TRACE_LARGE", data.Atlas.HasAnyLarge);

                _kernel.Bind(cmd, P.Large, data.Atlas.Large);
                _kernel.Bind(cmd, P.SDFAssets, data.Atlas.Assets);
                _kernel.Bind(cmd, P.DFInstances, data.Atlas.Buffer);
                _kernel.Bind(cmd, P.SDFBricks, data.Atlas.Bricks);
                _kernel.BindOnce(cmd, P.SDFAtlas, data.Atlas.SDF);

                for (int i = 0; i < _lights.Directional.Count; i++)
                {
                    var light = _lights.Directional[i];
                    var forward = light.transform.forward;
                    _kernel.Setf(cmd, P.LightColor, light.color.r * light.intensity, light.color.g * light.intensity, light.color.b * light.intensity);
                    _kernel.Setf(cmd, P.LightDirection, forward.x, forward.y, forward.z);

                    _kernel.Dispatch(cmd, _surfels.UpdateArguments);
                }

                pass.End();
            }
        }
    }
}