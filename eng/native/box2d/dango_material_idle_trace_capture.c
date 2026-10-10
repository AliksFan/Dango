// Fixed private observation only. No local world implementation or fallback.
#define _CRT_SECURE_NO_WARNINGS
#define DANGO_API
#include "dango_physics_module.h"
#include "dango_material_idle_trace_format.h"
#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stddef.h>
#include <math.h>

#define CAPTURE_DIAGNOSTIC_BYTES 87016u
#define CAPTURE_JSON_LIMIT (UINT64_C(256) * 1024)
_Static_assert(sizeof(dango_self_physics_config) == 164, "fixed config layout");
_Static_assert(sizeof(void*) == 8, "private capture is x64 only");

typedef int (*capture_abi_fn)(void);
typedef int (*capture_layout_fn)(dango_it_layout*);
typedef int (*capture_create_fn)(const dango_self_physics_config*, int, void**);
typedef int (*capture_add_fn)(void*, float, float, int*);
typedef int (*capture_begin_fn)(void*, const char*, int);
typedef int (*capture_step_fn)(void*, float, const int*, const float*, int, int*, float*, int, int*, int*, int*);
typedef int (*capture_diagnostic_fn)(void*, void*);
typedef int (*capture_status_fn)(void*);
typedef int (*capture_finish_fn)(void*);
typedef int (*capture_destroy_fn)(void*);

typedef struct capture_guarded_layout
{
    uint64_t before[4];
    dango_it_layout value;
    uint64_t after[4];
} capture_guarded_layout;
_Static_assert(offsetof(capture_guarded_layout, value) == 32, "layout leading guard");
_Static_assert(offsetof(capture_guarded_layout, after) == 32 + sizeof(dango_it_layout), "layout trailing guard");

// Exact idle_defaults(true) fixture; all other original-default fields retained.
static dango_self_physics_config capture_defaults(void)
{
    dango_self_physics_config config = {
        .node_count = 16, .substep_count = 4, .radial_rest_length = .68f,
        .radial_minimum_ratio = .65f, .radial_maximum_ratio = 1.22f,
        .perimeter_minimum_ratio = .73f, .perimeter_maximum_ratio = 1.07f,
        .radial_hertz = 1.82f, .perimeter_hertz = 15.075f,
        .radial_damping = 1.85f, .perimeter_damping = 1.85f,
        .area_strength = 4.8f, .ground_deformation_strength = 0,
        .repulsion_distance = .09f, .repulsion_strength = .3f,
        .repulsion_maximum_force = 3.5f, .core_exclusion_ratio = .25f,
        .angle_strength = 10, .local_angle_balance_strength = 1.7f,
        .top_inward_force = 0, .gravity = 9.8f, .core_mass = .12f,
        .node_mass = .015f, .drop_height = .42f, .ground_height = -.72f,
        .node_radius = .045f, .friction = .72f, .restitution = .02f,
        .contact_hertz = 28, .linear_damping = 1.8f, .drag_stiffness = 10,
        .drag_damping = 2.2f, .drag_maximum_force = 8, .horizontal_force = 2.4f,
        .maximum_horizontal_speed = 3.2f, .air_control_ratio = .35f,
        .downward_force = 2.2f, .jump_impulse = .42f,
        .jump_cooldown_seconds = .45f, .maximum_speed = 12, .minimum_area = .42f
    };
    return config;
}

static bool capture_absolute(const char* path)
{
    size_t length = strlen(path);
    return (length >= 3 && ((path[0] >= 'A' && path[0] <= 'Z') ||
        (path[0] >= 'a' && path[0] <= 'z')) && path[1] == ':' &&
        (path[2] == '/' || path[2] == '\\')) ||
        (length >= 3 && path[0] == '\\' && path[1] == '\\');
}

static void capture_json_string(FILE* output, const char* value)
{
    fputc('"', output);
    for (const unsigned char* p = (const unsigned char*)value; *p; ++p)
    {
        if (*p == '"' || *p == '\\') { fputc('\\', output); fputc(*p, output); }
        else if (*p < 32) fprintf(output, "\\u%04x", (unsigned int)*p);
        else fputc(*p, output);
    }
    fputc('"', output);
}

static void capture_hex(FILE* output, const void* value, size_t bytes)
{
    const unsigned char* data = value;
    fputc('"', output);
    for (size_t i = 0; i < bytes; ++i) fprintf(output, "%02x", (unsigned int)data[i]);
    fputc('"', output);
}

static bool capture_layout_guard(const capture_guarded_layout* guard)
{
    for (int i = 0; i < 4; ++i)
        if (guard->before[i] != UINT64_C(0xE54BF22C31A7086D) ||
            guard->after[i] != UINT64_C(0x7A019CBBD385E246)) return false;
    return true;
}

static bool capture_diagnostic_guard(const unsigned char* guard, size_t bytes)
{
    for (size_t i = 0; i < 32; ++i)
        if (guard[i] != 0x3c || guard[32 + bytes + i] != 0xc3) return false;
    return true;
}

static uint64_t capture_raw_worst_bytes(void)
{
    uint64_t prefixes = sizeof(dango_it_prefix);
    return sizeof(dango_it_header) * UINT64_C(2) +
        DANGO_IT_STEPS * (prefixes + sizeof(dango_it_step) + UINT64_C(32) * sizeof(dango_it_mode)) +
        DANGO_IT_STEPS * (prefixes + sizeof(dango_it_cost)) +
        UINT64_C(2) * DANGO_IT_FRAMES * (prefixes + sizeof(dango_it_frame)) +
        UINT64_C(2) * 258 * (prefixes + sizeof(dango_it_window) + CAPTURE_DIAGNOSTIC_BYTES) +
        UINT64_C(2) * (prefixes + sizeof(dango_it_end));
}

int main(int argc, char** argv)
{
    if (argc != 5 || (strcmp(argv[3], "0") && strcmp(argv[3], "1")) ||
        !capture_absolute(argv[1]) || !capture_absolute(argv[2]) || !capture_absolute(argv[4]))
    {
        fputs("usage: capture absolute-dll-path new-absolute-trace-path 0|1 new-absolute-json-path\n", stderr);
        return 2;
    }
    FILE* output = fopen(argv[4], "wx");
    if (!output) { fputs("JSON output must be a new writable file\n", stderr); return 2; }
    int result = 4, enabled = argv[3][0] == '1', abi = -1;
    int layout_status = -999, create_status = -999, add_status = -999, begin_status = -999;
    int step_status = -999, diagnostic_status = -999, trace_status = -999, finish_status = -999, destroy_status = -999;
    int completed = 0, attempted = 0, entity_id = 0, state_id = 0, state_count = 0, contacts = 0, latched = 0;
    int gate_samples = 0, first_gate_frame = 0;
    unsigned int step_calls = 0, diagnostic_calls = 0, status_calls = 0, finish_calls = 0, destroy_calls = 0;
    bool begin_attempted = false, layout_valid = false, layout_canary = false;
    bool diagnostic_before = false, diagnostic_after = false, last_frame_published = false;
    bool unload_attempted = false, unload_succeeded = false;
    const char* failure = "none";
    HMODULE module = NULL;
    void* world = NULL;
    unsigned char* diagnostic = NULL;
    capture_guarded_layout guarded_layout;
    memset(&guarded_layout, 0, sizeof guarded_layout);
    for (int i = 0; i < 4; ++i)
    {
        guarded_layout.before[i] = UINT64_C(0xE54BF22C31A7086D);
        guarded_layout.after[i] = UINT64_C(0x7A019CBBD385E246);
    }
    dango_self_physics_config config = capture_defaults();
    float controls[6] = {0}, last_row[42] = {0};
    double low[4] = {INFINITY, INFINITY, INFINITY, INFINITY};
    double high[4] = {-INFINITY, -INFINITY, -INFINITY, -INFINITY}, first_spans[4] = {0};
    LARGE_INTEGER started = {0}, ended = {0}, frequency = {0};
    bool timing = QueryPerformanceFrequency(&frequency) && QueryPerformanceCounter(&started);
    capture_abi_fn actual_abi = NULL;
    capture_layout_fn actual_layout = NULL;
    capture_create_fn actual_create = NULL;
    capture_add_fn actual_add = NULL;
    capture_begin_fn actual_begin = NULL;
    capture_step_fn actual_step = NULL;
    capture_diagnostic_fn actual_diagnostic = NULL;
    capture_status_fn actual_status = NULL;
    capture_finish_fn actual_finish = NULL;
    capture_destroy_fn actual_destroy = NULL;
    module = LoadLibraryExA(argv[1], NULL, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    if (!module) { failure = "load-library"; goto cleanup; }
#define CAPTURE_BIND(function, name) do { \
    FARPROC address = GetProcAddress(module, name); \
    if (!address || sizeof(function) != sizeof(address)) { failure = "missing-export"; fprintf(stderr, "missing export: %s\n", name); goto cleanup; } \
    memcpy(&(function), &address, sizeof(function)); \
} while (0)
    CAPTURE_BIND(actual_abi, "dango_physics_get_abi_version");
    CAPTURE_BIND(actual_layout, "dango_material_idle_trace_layout");
    CAPTURE_BIND(actual_create, "dango_physics_world_create");
    CAPTURE_BIND(actual_add, "dango_physics_world_add");
    CAPTURE_BIND(actual_begin, "dango_material_idle_trace_begin");
    CAPTURE_BIND(actual_step, "dango_physics_world_step");
    CAPTURE_BIND(actual_diagnostic, "dango_material_contact_get_diagnostic");
    CAPTURE_BIND(actual_status, "dango_material_idle_trace_status");
    CAPTURE_BIND(actual_finish, "dango_material_idle_trace_finish");
    CAPTURE_BIND(actual_destroy, "dango_physics_world_destroy");
#undef CAPTURE_BIND
    abi = actual_abi();
    if (abi != DANGO_PHYSICS_MODULE_ABI_VERSION) { failure = "public-abi"; goto cleanup; }
    layout_status = actual_layout(&guarded_layout.value);
    layout_canary = capture_layout_guard(&guarded_layout);
    const dango_it_layout* layout = &guarded_layout.value;
    layout_valid = layout_status == 0 && layout_canary && layout->schema == 1 &&
        layout->header_bytes == sizeof(dango_it_header) && layout->step_bytes == sizeof(dango_it_step) &&
        layout->mode_bytes == sizeof(dango_it_mode) && layout->frame_bytes == sizeof(dango_it_frame) &&
        layout->cost_bytes == sizeof(dango_it_cost) && layout->diagnostic_bytes == CAPTURE_DIAGNOSTIC_BYTES &&
        layout->config_bytes == sizeof(config) &&
        layout->input_offset < layout->positioned_offset && layout->positioned_offset < layout->damped_offset &&
        layout->damped_offset < layout->uncapped_offset && layout->uncapped_offset < layout->output_offset &&
        layout->output_offset < layout->diagnostic_bytes && layout->positions_offset < layout->velocities_offset &&
        layout->velocities_offset < layout->inverse_mass_offset &&
        layout->worst_case_bytes <= DANGO_IT_LIMIT && capture_raw_worst_bytes() + UINT64_C(2) * CAPTURE_JSON_LIMIT <= DANGO_IT_LIMIT;
    if (!layout_valid) { failure = "private-layout-or-capacity"; goto cleanup; }
    diagnostic = malloc((size_t)layout->diagnostic_bytes + 64);
    if (!diagnostic) { failure = "diagnostic-allocation"; goto cleanup; }
    memset(diagnostic, 0x3c, 32);
    memset(diagnostic + 32, 0xa5, layout->diagnostic_bytes);
    memset(diagnostic + 32 + layout->diagnostic_bytes, 0xc3, 32);
    create_status = actual_create(&config, 1, &world);
    if (create_status || !world) { failure = "world-create"; goto cleanup; }
    add_status = actual_add(world, 0, 0, &entity_id);
    if (add_status || entity_id <= 0) { failure = "world-add"; goto cleanup; }
    begin_attempted = true; begin_status = actual_begin(world, argv[2], enabled);
    if (begin_status) { failure = "trace-begin"; goto get_diagnostic; }
    for (int frame = 1; frame <= DANGO_IT_FRAMES; ++frame)
    {
        attempted = frame; last_frame_published = false;
        memset(controls, 0, sizeof controls);
        ++step_calls;
        step_status = actual_step(world, .0166666f, &entity_id, controls, 1, &state_id,
            last_row, 42, &state_count, &contacts, &latched);
        ++status_calls; trace_status = actual_status(world);
        if (step_status || latched || state_count != 1 || state_id != entity_id)
        { failure = "world-step-or-publication"; goto get_diagnostic; }
        for (int i = 0; i < 42; ++i)
            if (!isfinite(last_row[i])) { failure = "nonfinite-public-row"; goto get_diagnostic; }
        ++completed; last_frame_published = true;
        if (trace_status) { failure = "observation-status"; goto get_diagnostic; }
        int index = frame - 1;
        if (index >= 600 && (index % 60 == 0 || index >= 35400))
        {
            double min_x = INFINITY, min_y = INFINITY, max_x = -INFINITY, max_y = -INFINITY;
            for (int node = 0; node < 16; ++node)
            {
                min_x = fmin(min_x, last_row[2*node]); max_x = fmax(max_x, last_row[2*node]);
                min_y = fmin(min_y, last_row[2*node+1]); max_y = fmax(max_y, last_row[2*node+1]);
            }
            double values[4] = {last_row[32], last_row[33], max_x-min_x, max_y-min_y};
            for (int i = 0; i < 4; ++i) { low[i] = fmin(low[i], values[i]); high[i] = fmax(high[i], values[i]); }
            ++gate_samples;
            if (!first_gate_frame)
                for (int i = 0; i < 4; ++i)
                    if (high[i] - low[i] >= .003)
                    {
                        first_gate_frame = frame;
                        for (int j = 0; j < 4; ++j) first_spans[j] = high[j] - low[j];
                        break;
                    }
        }
    }
get_diagnostic:
    diagnostic_before = capture_diagnostic_guard(diagnostic, guarded_layout.value.diagnostic_bytes);
    if (diagnostic_before)
    {
        ++diagnostic_calls;
        diagnostic_status = actual_diagnostic(world, diagnostic + 32);
        diagnostic_after = capture_diagnostic_guard(diagnostic, guarded_layout.value.diagnostic_bytes);
    }
    if (strcmp(failure, "none") == 0)
    {
        if (diagnostic_status || !diagnostic_before || !diagnostic_after) failure = "diagnostic-read-or-canary";
        else if (completed != DANGO_IT_FRAMES || gate_samples != 96 || first_gate_frame != DANGO_IT_FRAMES)
            failure = "fixed-trajectory-gate-mismatch";
    }
cleanup:
    if (world && begin_attempted && actual_finish)
    {
        ++finish_calls; finish_status = actual_finish(world);
        if (finish_status && strcmp(failure, "none") == 0) failure = "trace-finish";
    }
    if (world && actual_destroy)
    {
        ++destroy_calls; destroy_status = actual_destroy(world);
        if (destroy_status && strcmp(failure, "none") == 0) failure = "world-destroy";
        if (!destroy_status) world = NULL;
    }
    if (module)
    {
        unload_attempted = true; unload_succeeded = FreeLibrary(module) != 0;
        if (!unload_succeeded && strcmp(failure, "none") == 0) failure = "unload-library";
        if (unload_succeeded) module = NULL;
    }
    if (timing) timing = QueryPerformanceCounter(&ended) != 0;
    if (strcmp(failure, "none") == 0) result = 0;
    fprintf(output, "{\"schema\":1,\"actualDll\":true,\"enabled\":%d,\"dllPath\":", enabled);
    capture_json_string(output, argv[1]); fputs(",\"tracePath\":", output); capture_json_string(output, argv[2]);
    fputs(",\"failure\":", output); capture_json_string(output, failure);
    fprintf(output, ",\"result\":%d,\"requestedFrames\":%d,\"expectedSubsteps\":%d,\"completedFrames\":%d,\"attemptedFrame\":%d,\"error\":%d,\"latchedError\":%d,\"nativeAbiVersion\":%d,\"layoutStatus\":%d,\"layoutValid\":%s,\"layoutCanaryPassed\":%s,\"layout\":{\"schema\":%u,\"headerBytes\":%u,\"stepBytes\":%u,\"modeBytes\":%u,\"frameBytes\":%u,\"diagnosticBytes\":%u,\"configBytes\":%u,\"costBytes\":%u,\"worstCaseBytes\":%llu,\"inputOffset\":%u,\"positionedOffset\":%u,\"dampedOffset\":%u,\"uncappedOffset\":%u,\"outputOffset\":%u,\"positionsOffset\":%u,\"velocitiesOffset\":%u,\"inverseMassOffset\":%u},\"independentRawWorstBytes\":%llu,\"captureJsonLimitBytes\":%llu,\"evidenceLimitBytes\":%llu",
        result, DANGO_IT_FRAMES, DANGO_IT_STEPS, completed, attempted, step_status, latched, abi, layout_status,
        layout_valid ? "true" : "false", layout_canary ? "true" : "false", guarded_layout.value.schema,
        guarded_layout.value.header_bytes, guarded_layout.value.step_bytes, guarded_layout.value.mode_bytes,
        guarded_layout.value.frame_bytes, guarded_layout.value.diagnostic_bytes, guarded_layout.value.config_bytes,
        guarded_layout.value.cost_bytes, (unsigned long long)guarded_layout.value.worst_case_bytes,
        guarded_layout.value.input_offset, guarded_layout.value.positioned_offset, guarded_layout.value.damped_offset,
        guarded_layout.value.uncapped_offset, guarded_layout.value.output_offset, guarded_layout.value.positions_offset,
        guarded_layout.value.velocities_offset, guarded_layout.value.inverse_mass_offset,
        (unsigned long long)capture_raw_worst_bytes(), (unsigned long long)CAPTURE_JSON_LIMIT,
        (unsigned long long)DANGO_IT_LIMIT);
    fprintf(output, ",\"calls\":{\"create\":%d,\"add\":%d,\"begin\":%d,\"step\":%u,\"status\":%u,\"diagnostic\":%u,\"finish\":%u,\"destroy\":%u},\"createStatus\":%d,\"addStatus\":%d,\"beginStatus\":%d,\"observerStatus\":%d,\"getDiagnosticStatus\":%d,\"finishStatus\":%d,\"destroyStatus\":%d,\"libraryUnloadAttempted\":%s,\"libraryFreed\":%s,\"worldDestroyConfirmed\":%s,\"lastAttemptedFramePublished\":%s,\"traceFinishSucceeded\":%s,\"canaryBeforePassed\":%s,\"canaryAfterPassed\":%s,\"allWorldCallsUseGetProcAddress\":true",
        create_status != -999, add_status != -999, begin_status != -999, step_calls, status_calls, diagnostic_calls, finish_calls,
        destroy_calls, create_status, add_status, begin_status, trace_status, diagnostic_status, finish_status, destroy_status,
        unload_attempted ? "true" : "false", unload_succeeded ? "true" : "false", destroy_status == 0 ? "true" : "false",
        last_frame_published ? "true" : "false", finish_status == 0 ? "true" : "false",
        diagnostic_before ? "true" : "false", diagnostic_after ? "true" : "false");
    fprintf(output, ",\"gateSampleCount\":%d,\"gateSpanLimit\":0.003,\"firstGateFrame\":%d,\"firstGateSpans\":[", gate_samples, first_gate_frame);
    for (int i = 0; i < 4; ++i) fprintf(output, "%s%.17g", i ? "," : "", first_spans[i]);
    fputs("],\"nativeRow\":[", output);
    for (int i = 0; i < 42; ++i)
        if (isfinite(last_row[i])) fprintf(output, "%s%.9g", i ? "," : "", (double)last_row[i]);
        else fprintf(output, "%snull", i ? "," : "");
    fputs("],\"nativeRowBits\":", output); capture_hex(output, last_row, sizeof last_row);
    fputs(",\"configRawHex\":", output); capture_hex(output, &config, sizeof config);
    float step = .0166666f;
    fputs(",\"nativeStepFloatBits\":", output); capture_hex(output, &step, sizeof step);
    fputs(",\"lastDiagnosticRawHex\":", output);
    if (diagnostic_status == 0 && diagnostic_before && diagnostic_after)
        capture_hex(output, diagnostic + 32, guarded_layout.value.diagnostic_bytes);
    else fputs("null", output);
    fputs(",\"elapsedSeconds\":", output);
    if (timing) fprintf(output, "%.17g", (double)(ended.QuadPart - started.QuadPart) / (double)frequency.QuadPart);
    else fputs("null", output);
    fputs(",\"privateLayoutScope\":\"Matching trace schema and explicit diagnostic size; guards and public ABI are supplemental, not general private-ABI negotiation.\"}\n", output);
    if (ferror(output) || _ftelli64(output) < 0 || (uint64_t)_ftelli64(output) > CAPTURE_JSON_LIMIT) result = 5;
    free(diagnostic);
    if (fclose(output)) result = 5;
    printf("actual idle trace enabled=%d completed=%d firstGate=%d failure=%s result=%d\n", enabled, completed, first_gate_frame, failure, result);
    return result;
}
