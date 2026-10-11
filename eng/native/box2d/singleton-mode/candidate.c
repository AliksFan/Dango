#include "../endpoint-selection/candidate.c"
// Current and retained event are separate observations. The latter never
// authorizes a physical retry and is explicitly marked current or historical.
DANGO_API int dango_singleton_mode_get_trace(void* handle,void* output,int capacity,int* bytes)
{
    if(!handle||!bytes) return -410;*bytes=(int)sizeof(dango_singleton_trace);
    if(!output||capacity<*bytes) return -410;
    collision_review* world=handle;memcpy(output,&world->candidate_diagnostic.singleton,sizeof(dango_singleton_trace));return 0;
}
DANGO_API int dango_singleton_mode_get_last_event(void* handle,void* output,int capacity,int* bytes,int* is_current)
{
    if(!handle||!bytes||!is_current) return -410;*bytes=(int)sizeof(dango_singleton_trace);
    if(!output||capacity<*bytes) return -410;
    collision_review* world=handle;const dango_singleton_trace* last=&world->singleton_last;
    *is_current=last->original_failure_available&&world->candidate_diagnostic.singleton.original_failure_available&&
        last->substep==world->candidate_diagnostic.singleton.substep&&last->frame_token==world->candidate_diagnostic.singleton.frame_token;
    memcpy(output,last,sizeof *last);return 0;
}
DANGO_API int dango_singleton_mode_get_status(void* handle,uint64_t* modes,uint64_t* neighbors,int* bytes)
{
    if(!handle||!modes||!neighbors||!bytes) return -410;
    collision_review* world=handle;*modes=world->singleton_mode_attempts_total;
    *neighbors=world->endpoint_neighbor_attempts_total;*bytes=(int)sizeof(dango_singleton_trace);return 0;
}
