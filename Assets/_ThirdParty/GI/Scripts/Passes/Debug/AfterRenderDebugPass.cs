using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class AfterRenderDebugPass : Pass
    {
        private ScreenProbes _screen_probes;
        private Pass _debug_screen_traces;

        public AfterRenderDebugPass(ScreenProbes screen_probes)
        {
            _screen_probes = screen_probes;
        }

        public override int InputRequirements(GISettings settings)
        {
            int result = 0;

            if (_debug_screen_traces != null) result |= _debug_screen_traces.InputRequirements(settings);

            return result;
        }

        public override void Release()
        {
            if (_debug_screen_traces != null)
            {
                _debug_screen_traces.Release();
                _debug_screen_traces = null;
            }
        }

        public override void Execute(Executor executor, PassData data)
        {
            switch (data.Atlas.Debug)
            {
                case DebugChoice.ScreenTraces:
                    if (data.Settings.Experimental.ScreenProbes)
                    {
                        if (_debug_screen_traces == null)
                            _debug_screen_traces = new ScreenProbeDebugPass(_screen_probes);

                        _debug_screen_traces.Execute(executor, data);
                    }

                    break;
            }
        }
    }
}