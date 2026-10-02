#ifndef REDFASTGI_DEFINES_INCLUDED
#define REDFASTGI_DEFINES_INCLUDED

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

static const float DEPTH_EPSILON_ZERO = 0.00001f;
static const float DEPTH_EPSILON_ONE  = 0.99999f;

// avoid clashing with some other constant
static const float GI_PI = 3.1415926535897932f;

static const uint SH2_STRIDE = 48;
static const uint SH3_STRIDE = 112;

static const float MAX_EMISSION = 64.0f;

static const uint RADIANCE_STRIDE = 4 * 4;

static const uint FLAG_NEW = 0x10000000;
static const uint UNFLAG   = 0x00FFFFFF;

static const uint PROBE_SAMPLER_STRIDE = 4;

static const uint NORMAL_STEP = 256; // leaves plenty of space for many queries on the same probe
static const uint OFFSET_STEP = 256; // leaves plenty of space for many queries on the same probe

static const uint PROBE_LIVES = 6;
static const uint SURFEL_LIVES = 5;
static const uint FAR_PROBE_LIVES = 8;

static const uint SURFEL_MAP_STRIDE = 3 * 4;

static const uint PROBE_SCREEN_CELL_DIM = 8;

static const uint PROBE_SAMPLE_RES = 5;

static const float PROBE_QUERY_BIAS = 0.2f;

static const uint PROBE_CASCADE_DIM = 65;
static const uint PROBE_CASCADE_CELL_COUNT = PROBE_CASCADE_DIM * PROBE_CASCADE_DIM * PROBE_CASCADE_DIM;

static const uint PROBE_MAX_CASCADES = 8;

static const uint PROBE_RES = 16;
static const uint PROBE_RAY_COUNT = PROBE_RES * PROBE_RES;

static const uint PROBE_ATLAS_COUNT_X = 113;
static const uint PROBE_ATLAS_COUNT_Y = 113;

static const uint PROBE_MAX_ENTRIES  = PROBE_ATLAS_COUNT_X * PROBE_ATLAS_COUNT_Y;
static const uint PROBE_MAX_REQUESTS = 16384;

static const uint PROBE_MAX_RAYS = PROBE_MAX_ENTRIES * PROBE_RAY_COUNT;

static const float PROBE_MAX_OFFSET = 0.3f;

static const uint PROBE_ENTRY_POSITION = 0;
static const uint PROBE_ENTRY_CELL = 12;
static const uint PROBE_ENTRY_AGE = 16;
static const uint PROBE_ENTRY_LIFE = 20;
static const uint PROBE_ENTRY_NORMAL = 24;
static const uint PROBE_ENTRY_OFFSET = 28;
static const uint PROBE_ENTRY_DISTANCE = 32;
static const uint PROBE_ENTRY_USED_VIEW = 36;
static const uint PROBE_ENTRY_USED_OFFSET = 40;
static const uint PROBE_ENTRY_OFFSET_COUNT = 44;
static const uint PROBE_ENTRY_NORMAL_VALUES = 48;
static const uint PROBE_ENTRY_OFFSET_VALUES = 72;
static const uint PROBE_ENTRY_STRIDE = 96;

static const uint PROBE_COUNT_ENTRY = 0;
static const uint PROBE_COUNT_POOL = 1;
static const uint PROBE_COUNT_REMOVE = 2;
static const uint PROBE_COUNT_REQUESTS = 3;
static const uint PROBE_COUNT_NEW = 4;
static const uint PROBE_COUNT_LIVES = 5;
static const uint PROBE_COUNT_TEMP = 6;
static const uint PROBE_COUNT_MAX = 7; // debug
static const uint PROBE_COUNT_ALL = 8;

static const uint PROBE_ARRAY_ENTRY = 0;
static const uint PROBE_ARRAY_POOL = PROBE_MAX_ENTRIES;
static const uint PROBE_ARRAY_REMOVE = PROBE_MAX_ENTRIES * 2;
static const uint PROBE_ARRAY_TEMP = PROBE_MAX_ENTRIES * 3;
static const uint PROBE_ARRAY_ALL = PROBE_ARRAY_TEMP + PROBE_MAX_REQUESTS; // make ensure max requests is larger than max_entries

static const float PROBE_BLEND_RANGE = PROBE_CASCADE_DIM * 0.5;
static const float PROBE_BLEND_THRESHOLD = 0.15;
static const float PROBE_BLEND_BORDER = 1.0 - PROBE_BLEND_THRESHOLD;

static const uint FAR_PROBE_RES = 32;
static const uint FAR_PROBE_RAY_COUNT = FAR_PROBE_RES * FAR_PROBE_RES;

static const uint FAR_PROBE_ATLAS_COUNT_X = 64;
static const uint FAR_PROBE_ATLAS_COUNT_Y = 32;

static const uint FAR_PROBE_ATLAS_WIDTH = FAR_PROBE_RES * FAR_PROBE_ATLAS_COUNT_X;
static const uint FAR_PROBE_ATLAS_HEIGHT = FAR_PROBE_RES * FAR_PROBE_ATLAS_COUNT_Y;

static const uint FAR_PROBE_MAX_ENTRIES = FAR_PROBE_ATLAS_COUNT_X * FAR_PROBE_ATLAS_COUNT_Y;
static const uint FAR_PROBE_MAX_REQUESTS = 2048;

static const uint FAR_PROBE_MAX_RAYS = FAR_PROBE_MAX_ENTRIES * FAR_PROBE_RAY_COUNT;

static const uint FAR_PROBE_RADIANCE_COUNT = FAR_PROBE_MAX_ENTRIES * PROBE_RAY_COUNT;

static const uint FAR_PROBE_ARRAY_ENTRY = 0;
static const uint FAR_PROBE_ARRAY_POOL = FAR_PROBE_MAX_ENTRIES;
static const uint FAR_PROBE_ARRAY_REMOVE = FAR_PROBE_MAX_ENTRIES * 2;
static const uint FAR_PROBE_ARRAY_TEMP = FAR_PROBE_MAX_ENTRIES * 3;
static const uint FAR_PROBE_ARRAY_ALL = FAR_PROBE_ARRAY_TEMP + FAR_PROBE_MAX_REQUESTS;

static const uint FAR_PROBE_COUNT_ENTRY = 0;
static const uint FAR_PROBE_COUNT_POOL = 1;
static const uint FAR_PROBE_COUNT_REMOVE = 2;
static const uint FAR_PROBE_COUNT_REQUESTS = 3;
static const uint FAR_PROBE_COUNT_NEW = 4;
static const uint FAR_PROBE_COUNT_LIVES = 5;
static const uint FAR_PROBE_COUNT_TEMP = 6;
static const uint FAR_PROBE_COUNT_MAX = 7; // debug
static const uint FAR_PROBE_COUNT_ALL = 8;

static const uint FAR_PROBE_CASCADE_DIM = 17;
static const uint FAR_PROBE_MAX_CASCADES = 8;
static const float FAR_PROBE_SPACING = 4.0f; // compares to normal probes
static const uint FAR_PROBE_CASCADE_CELL_COUNT = FAR_PROBE_CASCADE_DIM * FAR_PROBE_CASCADE_DIM * FAR_PROBE_CASCADE_DIM;

static const uint FAR_PROBE_ENTRY_POSITION = 0;
static const uint FAR_PROBE_ENTRY_CELL = 12;
static const uint FAR_PROBE_ENTRY_AGE = 16;
static const uint FAR_PROBE_ENTRY_LIFE = 20;
static const uint FAR_PROBE_ENTRY_STRIDE = 24;

static const uint TRACE_STRIDE = 1 * 4;
static const uint HIT_STRIDE = 8 * 4;
static const uint HIT_POSITION = 4 * 4;

static const uint TRACE_COUNT_PROBE = 0;
static const uint TRACE_COUNT_FAR = 1;
static const uint TRACE_COUNT_SCREEN = 2;
static const uint TRACE_COUNT_ALL = 3;

static const uint TRACE_ARRAY_PROBE = 0;
static const uint TRACE_ARRAY_FAR = PROBE_MAX_RAYS;
static const uint TRACE_ARRAY_SCREEN = TRACE_ARRAY_FAR + FAR_PROBE_MAX_RAYS;

static const uint BRICK_DIM = 8;
static const uint BRICK_PAYLOAD = BRICK_DIM - 1;
static const float BRICK_PADDING = 0.5f / (float)BRICK_PAYLOAD;

static const uint INVALID_ID = 4294967295;
static const uint INVALID_U16 = 65535;

static const uint GDF_CELL_DIM = 8;
static const uint GDF_GRID_DIM = 64;

static const uint SDF_GRID_OFFSET = 0;
static const uint SDF_GRID_HASH = 8;
static const uint SDF_GRID_COUNT = 12;
static const uint SDF_GRID_STRIDE = 16;

static const uint SDF_GRID_INSTANCES = GDF_GRID_DIM * GDF_GRID_DIM * GDF_GRID_DIM * SDF_GRID_STRIDE;

static const uint SDF_MAX_INSTANCES = 131072;

static const uint SDF_REQUEST_COUNT = 0;
static const uint SDF_REQUEST_OFFSETS = SDF_REQUEST_COUNT + 4;
static const uint SDF_REQUEST_INDICES = SDF_REQUEST_OFFSETS + SDF_MAX_INSTANCES * 4;

static const uint SDF_LARGE_COUNT = 0;
static const uint SDF_LARGE_INDICES = 4;

static const uint MAX_ASSET_DIM = 64;
static const uint MAX_ATLAS_DIM = 512;

static const uint MAX_BRICKS = MAX_ATLAS_DIM * MAX_ATLAS_DIM * MAX_ATLAS_DIM / (BRICK_DIM * BRICK_DIM * BRICK_DIM);

static const uint ASSET_STRIDE = 48;

static const uint ATLAS_DIM = 256;

static const uint GDF_CASCADE_DIM  = 128;
static const uint GDF_MAX_CASCADES = 7;

static const float GDF_SDF_CELL_DIM = 0.2f;

static const float GDF_BASE_STEP = 4;

static const float GDF_SAMPLE_BIAS = 0.2;
static const float SDF_SAMPLE_BIAS = 0.05;

static const uint RAY_EMPTY = 4294967295;
static const float MAX_HIT_DISTANCE = 8192.0f;

static const uint FLOAT_STRIDE = 4;
static const uint FLOAT4_STRIDE = 16;

static const uint SURFEL_CASCADE_DIM = 65;
static const uint SURFEL_CASCADE_CELL_COUNT = SURFEL_CASCADE_DIM * SURFEL_CASCADE_DIM * SURFEL_CASCADE_DIM;

static const uint SURFEL_MAX_CASCADES = 12;
static const uint SURFEL_CASCADE_COUNT = 12;
static const uint SURFEL_CELL_COUNT = SURFEL_CASCADE_CELL_COUNT * SURFEL_CASCADE_COUNT;

static const float SURFEL_CELL_DIM = 0.125;

static const uint SURFEL_MAX_ENTRIES = 98304;
static const uint SURFEL_MAX_REQUESTS = 98304;

static const uint SURFEL_ENTRY_POSITION = 0;
static const uint SURFEL_ENTRY_CELL = 12;
static const uint SURFEL_ENTRY_AGE = 16;
static const uint SURFEL_ENTRY_NORMAL = 20;
static const uint SURFEL_ENTRY_COLOR = 24;
static const uint SURFEL_ENTRY_STRIDE = 28;

static const uint SURFEL_COUNT_ENTRY = 0;
static const uint SURFEL_COUNT_POOL = 1;
static const uint SURFEL_COUNT_REMOVE = 2;
static const uint SURFEL_COUNT_REQUESTS = 3;
static const uint SURFEL_COUNT_NEW = 4;
static const uint SURFEL_COUNT_LIVES = 5;
static const uint SURFEL_COUNT_TEMP = 6;
static const uint SURFEL_COUNT_MAX = 7; // debug
static const uint SURFEL_COUNT_ALL = 8;

static const uint SURFEL_ARRAY_ENTRY = 0;
static const uint SURFEL_ARRAY_POOL = SURFEL_MAX_ENTRIES;
static const uint SURFEL_ARRAY_REMOVE = SURFEL_MAX_ENTRIES * 2;
static const uint SURFEL_ARRAY_VERSION = SURFEL_MAX_ENTRIES * 3;
static const uint SURFEL_ARRAY_HIT = SURFEL_MAX_ENTRIES * 4;
static const uint SURFEL_ARRAY_TEMP = SURFEL_MAX_ENTRIES * 5;
static const uint SURFEL_ARRAY_ALL = SURFEL_ARRAY_TEMP + SURFEL_MAX_REQUESTS; // make ensure max requests is larger than max_entries

static const uint GROUP = 64;
static const uint GROUPS_PER_PROBE = PROBE_RAY_COUNT / GROUP;
static const uint GROUPS_PER_FAR_PROBE = FAR_PROBE_RAY_COUNT / GROUP;

static const uint SCREEN_PROBE_DIM = 16;
static const uint SCREEN_PROBE_ADAPTIVE_DIM = 4;

static const uint SCREEN_PROBE_POSITION = 0;
static const uint SCREEN_PROBE_NORMAL = 12;
static const uint SCREEN_PROBE_ORIGIN = 16;
static const uint SCREEN_PROBE_STRIDE = 32;

static const uint SCREEN_PROBE_RES = 8;
static const uint SCREEN_PROBE_RAY_COUNT = SCREEN_PROBE_RES * SCREEN_PROBE_RES;

static const uint SCREEN_PROBE_COUNT_BASE = 0;
static const uint SCREEN_PROBE_COUNT_ADAPTIVE = 1;
static const uint SCREEN_PROBE_COUNT_TOTAL = 2;
static const uint SCREEN_PROBE_COUNT_ALL = 3;

// if count buffers are corrupted, they might give us insane values, this is used to get back to some saner count to prevent frames taking seconds.
static const uint SCREEN_PROBE_RAY_SANITY_COUNT = 33554432;
static const uint SCREEN_PROBE_SANITY_COUNT = 524288;
static const uint REQUEST_SANITY_COUNT = 1048576;

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#endif