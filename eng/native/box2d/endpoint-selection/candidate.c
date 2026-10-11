#include "../receipt-history/candidate.c"

// Observation only; the original ABI-1 world API and old diagnostic prefix
// stay unchanged. This returns the complete original failed selection report.
DANGO_API int dango_endpoint_selection_get_trace(void* handle, void* output,
    int capacity, int* bytes)
{
    if (!handle || !bytes) return -410;
    *bytes = (int)sizeof(dango_endpoint_trace);
    if (!output || capacity < *bytes) return -410;
    collision_review* world = handle;
    const dango_endpoint_trace* trace=world->endpoint_last.original_failure_available ?
        &world->endpoint_last : &world->candidate_diagnostic.endpoint;
    memcpy(output, trace, sizeof(dango_endpoint_trace));
    return 0;
}

DANGO_API int dango_endpoint_selection_get_status(void* handle,
    uint64_t* attempts,uint64_t* failures,int* trace_bytes)
{
    if(!handle || !attempts || !failures || !trace_bytes) return -410;
    collision_review* world=handle;
    *attempts=world->endpoint_neighbor_attempts_total;
    *failures=world->endpoint_original_failures_total;
    *trace_bytes=(int)sizeof(dango_endpoint_trace);
    return 0;
}
