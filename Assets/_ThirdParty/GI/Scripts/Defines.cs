namespace GI
{
    public struct Defines
    {
        public static int RADIANCE_STRIDE = 4 * 4;

        public static int SH2_STRIDE = 48;
        public static int SH3_STRIDE = 112;

        public static int SURFEL_MAP_STRIDE = 3 * 4;

        public static int PROBE_SCREEN_CELL_DIM = 8;

        public static int PROBE_SAMPLER_STRIDE = 4;

        public static int PROBE_CASCADE_DIM = 65;
        public static int PROBE_MAX_CASCADES = 8;
        public static int PROBE_CASCADE_CELL_COUNT = PROBE_CASCADE_DIM * PROBE_CASCADE_DIM * PROBE_CASCADE_DIM;

        public static int PROBE_SAMPLE_RES = 5;
        public static int PROBE_SAMPLE_COUNT = PROBE_SAMPLE_RES * PROBE_SAMPLE_RES;

        public static int PROBE_RES = 16;
        public static int PROBE_RAY_COUNT = PROBE_RES * PROBE_RES;

        public static int PROBE_ATLAS_COUNT_X = 113; // avoid hitting >2048 atlas width
        public static int PROBE_ATLAS_COUNT_Y = 113;

        public static int PROBE_MAX_ENTRIES = PROBE_ATLAS_COUNT_X * PROBE_ATLAS_COUNT_Y;
        public static int PROBE_MAX_REQUESTS = 16384;

        public static int PROBE_ATLAS_WIDTH = PROBE_RES * PROBE_ATLAS_COUNT_X;
        public static int PROBE_ATLAS_HEIGHT = PROBE_RES * PROBE_ATLAS_COUNT_Y;

        public static int PROBE_DEPTH_ATLAS_WIDTH = (PROBE_RES + 2) * PROBE_ATLAS_COUNT_X;
        public static int PROBE_DEPTH_ATLAS_HEIGHT = (PROBE_RES + 2) * PROBE_ATLAS_COUNT_Y;

        public static int PROBE_MAX_RAYS = PROBE_MAX_ENTRIES * PROBE_RAY_COUNT;

        public static int PROBE_ENTRY_STRIDE = 96;

        public static int PROBE_COUNT_ENTRY = 0;
        public static int PROBE_COUNT_POOL = 1;
        public static int PROBE_COUNT_REMOVE = 2;
        public static int PROBE_COUNT_REQUESTS = 3;
        public static int PROBE_COUNT_NEW = 4;
        public static int PROBE_COUNT_LIVES = 5;
        public static int PROBE_COUNT_TEMP = 6;
        public static int PROBE_COUNT_MAX = 7;
        public static int PROBE_COUNT_ALL = 8;

        public static int PROBE_ARRAY_ENTRY = 0;
        public static int PROBE_ARRAY_POOL = PROBE_MAX_ENTRIES;
        public static int PROBE_ARRAY_REMOVE = PROBE_MAX_ENTRIES * 2;
        public static int PROBE_ARRAY_TEMP = PROBE_MAX_ENTRIES * 3;
        public static int PROBE_ARRAY_ALL = PROBE_ARRAY_TEMP + PROBE_MAX_REQUESTS;

        public static int FAR_PROBE_RES = 32;
        public static int FAR_PROBE_RAY_COUNT = FAR_PROBE_RES * FAR_PROBE_RES;

        public static int FAR_PROBE_ATLAS_COUNT_X = 64;
        public static int FAR_PROBE_ATLAS_COUNT_Y = 32;

        public static int FAR_PROBE_ATLAS_WIDTH = FAR_PROBE_RES * FAR_PROBE_ATLAS_COUNT_X;
        public static int FAR_PROBE_ATLAS_HEIGHT = FAR_PROBE_RES * FAR_PROBE_ATLAS_COUNT_Y;

        public static int FAR_PROBE_MAX_ENTRIES = FAR_PROBE_ATLAS_COUNT_X * FAR_PROBE_ATLAS_COUNT_Y;
        public static int FAR_PROBE_MAX_REQUESTS = 2048;

        public static int FAR_PROBE_MAX_RAYS = FAR_PROBE_MAX_ENTRIES * FAR_PROBE_RAY_COUNT;

        public static int FAR_PROBE_ARRAY_ENTRY = 0;
        public static int FAR_PROBE_ARRAY_POOL = FAR_PROBE_MAX_ENTRIES;
        public static int FAR_PROBE_ARRAY_REMOVE = FAR_PROBE_MAX_ENTRIES * 2;
        public static int FAR_PROBE_ARRAY_TEMP = FAR_PROBE_MAX_ENTRIES * 3;
        public static int FAR_PROBE_ARRAY_ALL = FAR_PROBE_ARRAY_TEMP + FAR_PROBE_MAX_REQUESTS;

        public static int FAR_PROBE_COUNT_ENTRY = 0;
        public static int FAR_PROBE_COUNT_POOL = 1;
        public static int FAR_PROBE_COUNT_REMOVE = 2;
        public static int FAR_PROBE_COUNT_REQUESTS = 3;
        public static int FAR_PROBE_COUNT_NEW = 4;
        public static int FAR_PROBE_COUNT_LIVES = 5;
        public static int FAR_PROBE_COUNT_TEMP = 6;
        public static int FAR_PROBE_COUNT_MAX = 7; // debug
        public static int FAR_PROBE_COUNT_ALL = 8;

        public static int FAR_PROBE_CASCADE_DIM = 17;
        public static int FAR_PROBE_MAX_CASCADES = 8;
        public static float FAR_PROBE_SPACING = 4.0f; // compares to normal probes
        public static int FAR_PROBE_CASCADE_CELL_COUNT = FAR_PROBE_CASCADE_DIM * FAR_PROBE_CASCADE_DIM * FAR_PROBE_CASCADE_DIM;

        public static int FAR_PROBE_ENTRY_POSITION = 0;
        public static int FAR_PROBE_ENTRY_CELL = 12;
        public static int FAR_PROBE_ENTRY_AGE = 16;
        public static int FAR_PROBE_ENTRY_LIFE = 20;
        public static int FAR_PROBE_ENTRY_STRIDE = 24;

        public static int TRACE_STRIDE = 1 * 4;
        public static int HIT_STRIDE = 8 * 4;
        public static int MAX_RAYS = PROBE_MAX_RAYS + FAR_PROBE_MAX_RAYS;

        public static int TRACE_COUNT_PROBE = 0;
        public static int TRACE_COUNT_FAR = 1;
        public static int TRACE_COUNT_SCREEN = 2;
        public static int TRACE_COUNT_ALL = 3;

        public static int TRACE_ARRAY_PROBE = 0;
        public static int TRACE_ARRAY_FAR = PROBE_MAX_RAYS;
        public static int TRACE_ARRAY_SCREEN = TRACE_ARRAY_FAR + FAR_PROBE_MAX_RAYS;

        public static int BRICK_DIM = 8;
        public static int BRICK_PAYLOAD = BRICK_DIM - 1;

        public static uint INVALID_ID = 4294967295;

        public static int MAX_ASSET_DIM = 64;
        public static int ATLAS_DIM = 256;

        public static int GDF_CASCADE_DIM  = 128;
        public static int GDF_MAX_CASCADES = 7;

        public static float GDF_SDF_CELL_DIM = 0.2f;

        public static int GDF_CELL_DIM = 8;
        public static int GDF_GRID_DIM = 64;

        public static int SDF_GRID_STRIDE = 16;
        public static int SDF_GRID_INSTANCES = GDF_GRID_DIM * GDF_GRID_DIM * GDF_GRID_DIM * SDF_GRID_STRIDE;
        public static int MAX_GRID_INSTANCES = 262144;
        public static int SDF_GRID_SIZE = SDF_GRID_INSTANCES + MAX_GRID_INSTANCES * 4;

        public static int MAX_GDF_UPDATES = 131072;

        public static int MAX_ASSETS = 32768;
        public static int MAX_BRICKS = ATLAS_DIM * ATLAS_DIM * ATLAS_DIM / (BRICK_DIM * BRICK_DIM * BRICK_DIM);

        public static int ASSET_STRIDE = 48;

        public static int SDF_MAX_INSTANCES = 131072;

        public static int SDF_REQUEST_COUNT = 0;
        public static int SDF_REQUEST_OFFSETS = SDF_REQUEST_COUNT + 4;
        public static int SDF_REQUEST_INDICES = SDF_REQUEST_OFFSETS + SDF_MAX_INSTANCES * 4;
        public static int SDF_REQUEST_SIZE = SDF_REQUEST_INDICES + SDF_MAX_INSTANCES * 8 * 4; // enough space for 8 cells per instance when MAX_INSTANCES

        public static int SDF_LARGE_COUNT = 0;
        public static int SDF_LARGE_INDICES = 4;

        public static int SDF_LARGE_SIZE = 4096;

        public static int SURFEL_CASCADE_DIM = 65;
        public static int SURFEL_CASCADE_CELL_COUNT = SURFEL_CASCADE_DIM* SURFEL_CASCADE_DIM * SURFEL_CASCADE_DIM;

        public static int SURFEL_CASCADE_COUNT = 12;
        public static int SURFEL_CELL_COUNT = SURFEL_CASCADE_CELL_COUNT * SURFEL_CASCADE_COUNT;

        public static float SURFEL_CELL_DIM = 0.125f;

        public static int SURFEL_MAX_ENTRIES = 98304;
        public static int SURFEL_MAX_REQUESTS = 98304;

        public static int SURFEL_ENTRY_POSITION = 0;
        public static int SURFEL_ENTRY_CELL = 12;
        public static int SURFEL_ENTRY_AGE = 16;
        public static int SURFEL_ENTRY_NORMAL = 20;
        public static int SURFEL_ENTRY_COLOR = 24;
        public static int SURFEL_ENTRY_STRIDE = 28;

        public static int SURFEL_COUNT_ENTRY = 0;
        public static int SURFEL_COUNT_POOL = 1;
        public static int SURFEL_COUNT_REMOVE = 2;
        public static int SURFEL_COUNT_REQUESTS = 3;
        public static int SURFEL_COUNT_NEW = 4;
        public static int SURFEL_COUNT_LIVES = 5;
        public static int SURFEL_COUNT_TEMP = 6;
        public static int SURFEL_COUNT_MAX = 7;
        public static int SURFEL_COUNT_ALL = 8;

        public static int SURFEL_ARRAY_ENTRY = 0;
        public static int SURFEL_ARRAY_POOL = SURFEL_MAX_ENTRIES;
        public static int SURFEL_ARRAY_REMOVE = SURFEL_MAX_ENTRIES * 2;
        public static int SURFEL_ARRAY_VERSION = SURFEL_MAX_ENTRIES * 3;
        public static int SURFEL_ARRAY_HIT = SURFEL_MAX_ENTRIES * 4;
        public static int SURFEL_ARRAY_TEMP = SURFEL_MAX_ENTRIES * 5;
        public static int SURFEL_ARRAY_ALL = SURFEL_ARRAY_TEMP + SURFEL_MAX_REQUESTS;

        public static int SCREEN_PROBE_DIM = 16;
        public static int SCREEN_PROBE_ADAPTIVE_DIM = 4;
        public static int SCREEN_PROBE_STRIDE = 32;
        public static int SCREEN_PROBE_RES = 8;
        public static int SCREEN_PROBE_RAY_COUNT = SCREEN_PROBE_RES * SCREEN_PROBE_RES;

        public static int SCREEN_PROBE_COUNT_BASE = 0;
        public static int SCREEN_PROBE_COUNT_ADAPTIVE = 1;
        public static int SCREEN_PROBE_COUNT_TOTAL = 2;
        public static int SCREEN_PROBE_COUNT_ALL = 3;
    }
}
