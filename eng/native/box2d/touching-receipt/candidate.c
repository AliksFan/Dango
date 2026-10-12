#include "../singleton-mode/candidate.c"
// New classification observation; retained world API and diagnostics prefix.
DANGO_API int dango_touching_receipt_get_status(void* handle,int* pending,int* bytes)
{
    if(!handle || !pending || !bytes)return -410;
    collision_review* w=handle;*pending=w->candidate_diagnostic.touching.pending_count;
    *bytes=(int)sizeof(dango_material_contact_diagnostic);return 0;
}
DANGO_API int dango_touching_receipt_get_events(void* handle,uint64_t* events,int* bytes)
{
    if(!handle || !events || !bytes)return -410;
    collision_review* w=handle;*events=w->touching_events;
    *bytes=(int)sizeof(dango_material_contact_diagnostic);return 0;
}
DANGO_API int dango_touching_receipt_get_last(void* handle,void* output,int capacity,int* bytes,int* is_current)
{
    if(!handle || !bytes || !is_current)return -410;
    *bytes=(int)sizeof(dango_material_contact_diagnostic);if(!output || capacity<*bytes)return -410;
    collision_review* w=handle;
    *is_current=w->touching_events && w->touching_last.substeps==w->candidate_diagnostic.substeps;
    memcpy(output,&w->touching_last,sizeof w->touching_last);return 0;
}
