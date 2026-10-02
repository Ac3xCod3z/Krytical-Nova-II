using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class SurfelLightPass : Pass
    {
        private Kernel _kernel;

        private GDF _gdf;
        private Lights _lights;
        private Probes _probes;
        private Surfels _surfels;

        public SurfelLightPass(GDF gdf, Probes probes, Surfels surfels, Lights lights)
        {
            _gdf = gdf;
            _probes = probes;
            _surfels = surfels;
            _lights = lights;

            _kernel = new Kernel(Resources.Load<ComputeShader>("Shaders/Surfels/Light"), "Main");
        }

        public override void Execute(Executor executor, PassData data)
        {
            if (_lights.Point.Count > 0)
            {
                var camera = data.Camera;
                var reference_position = camera.transform.position;
                var gdf_base_position = Util.GridCoordToPosition(Util.GridOriginCoord(reference_position, Defines.GDF_CELL_DIM, Defines.GDF_GRID_DIM), Defines.GDF_CELL_DIM);

                var pass = executor.Pass();
                var cmd = pass.Begin("Surfel Light Point");

                _probes.Parameters(cmd, _kernel);
                _surfels.Parameters(cmd, _kernel);
                _gdf.Parameters(cmd, _kernel);
                _gdf.Textures(cmd, _kernel);

                for (int i = 0; i < _lights.Point.Count; i++)
                {
                    var light = _lights.Point[i];
                    var position = light.transform.position;

                    _kernel.Setf(cmd, P.LightColor, light.color.r * light.intensity, light.color.g * light.intensity, light.color.b * light.intensity);
                    _kernel.Setf(cmd, P.LightPosition, position.x, position.y, position.z);
                    _kernel.Setf(cmd, P.LightRange, light.range);                    

                    _kernel.Dispatch(cmd, _surfels.UpdateArguments);
                }

                pass.End();
            }
        }
    }
}