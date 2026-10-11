#include "dango_box2d_bridge.c"
#include "dango_collision_review.c"

// Candidate-only observation. Does not change the public ABI-1 step contract.
DANGO_API int dango_receipt_history_get_status(void* handle,int* phase,int* status,uint64_t* events,uint64_t* sequence)
{
    if(!handle || !phase || !status || !events || !sequence) return -410;
    collision_review* p=handle; *phase=p->candidate_diagnostic.history_phase; *status=p->candidate_diagnostic.history_status;
    *events=p->receipt_history_events; *sequence=p->receipt_history.sequence; return 0;
}
