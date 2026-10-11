#ifndef DANGO_SINGLETON_MODE_H
#define DANGO_SINGLETON_MODE_H
// This header is inserted only after the retained block types and certified
// particular / original next-mode priority helper have been defined.
typedef struct dango_singleton_trace
{
    bool original_failure_available, qualified, next_mode_valid;
    bool block_passed, adapter_passed, substep_passed, snapshot_published;
    int mode_attempts, budget_kind, changed_column, changed_row, previous_mode, next_mode;
    int final_block_status, final_null_status, final_phase, final_error;
    uint64_t substep, sequence, frame_token;
    dango_material_contact_block_report original_failure;
} dango_singleton_trace;
typedef struct dango_singleton_context
{
    dango_material_contact_block* block;
    dango_singleton_trace* trace;
    dango_material_contact_solver reference;
    dango_material_contact_null_report observed;
    bool fresh_failure;
} dango_singleton_context;
#if defined(_MSC_VER)
static __declspec(thread) dango_singleton_context dango_singleton_current;
#else
static _Thread_local dango_singleton_context dango_singleton_current;
#endif
static void dango_singleton_observe(void* owner,const dango_material_contact_null_report* report,bool fresh,bool result)
{
    dango_singleton_context* c=owner;
    if(c!=&dango_singleton_current||!c->block||report!=&c->block->report.null_reaction) return;
    c->fresh_failure=fresh&&!result&&report->status==DANGO_MATERIAL_CONTACT_NULL_INTERVAL_EMPTY;
    if(c->fresh_failure) c->observed=*report;
}
static inline bool dango_singleton_begin(dango_singleton_trace* trace,dango_endpoint_trace* endpoint,dango_material_contact_block* block)
{
    if(!trace||!endpoint||!block||!block->built||block->attempted||dango_singleton_budget_current.owner||dango_singleton_current.block) return false;
    if(!dango_endpoint_begin(endpoint,&block->report.null_reaction)) return false;
    memset(trace,0,sizeof *trace);trace->changed_column=trace->changed_row=trace->previous_mode=trace->next_mode=-1;
    memset(&dango_singleton_current,0,sizeof dango_singleton_current);
    dango_singleton_current.block=block;dango_singleton_current.trace=trace;dango_singleton_current.reference=block->contact;
    dango_singleton_budget_current.owner=&dango_singleton_current;
    dango_singleton_budget_current.spent=0;dango_singleton_budget_current.observe=dango_singleton_observe;
    return true;
}
static inline void dango_singleton_end(bool adapter_passed)
{
    if(dango_singleton_current.trace&&dango_singleton_current.block)
    {
        dango_singleton_trace* t=dango_singleton_current.trace;
        t->budget_kind=dango_singleton_budget_current.spent;
        t->adapter_passed=adapter_passed;
        t->final_block_status=dango_singleton_current.block->report.status;
        t->final_null_status=dango_singleton_current.block->report.null_reaction.status;
        t->block_passed=dango_singleton_current.block->attempted&&t->final_block_status==DANGO_MATERIAL_CONTACT_BLOCK_OK;
    }
    memset(&dango_singleton_current,0,sizeof dango_singleton_current);
    memset(&dango_singleton_budget_current,0,sizeof dango_singleton_budget_current);
    dango_endpoint_end();
}
static inline bool dango_singleton_qualified(const dango_material_contact_solver* reference,dango_material_contact_block_report* report)
{
    dango_singleton_context* c=&dango_singleton_current;
    if(dango_singleton_budget_current.owner!=c||!c->block||!c->block->built||!c->block->attempted||!c->trace||report!=&c->block->report||
       dango_endpoint_current.report!=&report->null_reaction||!dango_endpoint_current.trace||
       !c->fresh_failure||memcmp(&c->observed,&report->null_reaction,sizeof c->observed)!=0||
       !reference||memcmp(reference,&c->reference,sizeof *reference)!=0||
       memcmp(&c->block->contact.geometry,&c->reference.geometry,sizeof c->reference.geometry)!=0||
       c->block->contact.receipt_count!=c->reference.receipt_count||c->block->contact.step_seconds!=c->reference.step_seconds||
       memcmp(c->block->contact.receipts,c->reference.receipts,sizeof c->reference.receipts)!=0) return false;
    const dango_material_contact_null_report* n=&report->null_reaction;
    if(!c->trace->original_failure_available) {c->trace->original_failure=*report;c->trace->original_failure_available=true;}
    if(dango_singleton_budget_current.spent!=0||dango_endpoint_current.trace->neighbor_attempts!=0||c->trace->mode_attempts!=0||
       n->status!=DANGO_MATERIAL_CONTACT_NULL_INTERVAL_EMPTY||n->used||n->selected_pair_available||n->selected_checks_passed||
       !n->selection_available||n->selection_passed||!n->lower_bounded||!n->upper_bounded||
       !isfinite(n->lower)||!isfinite(n->upper)||n->lower!=n->upper||n->selected_alpha!=n->lower||
       !n->factor_available||!n->factor_passed||!n->raw_pair_available||!n->effective_pair_available||
       !n->pair_checks_available||!n->pair_checks_passed||!n->reaction_checks_available||!n->reaction_checks_passed||
       !n->interval_available||!n->interval_passed||n->mode_index<0||
       n->dimension<1||n->dimension>DANGO_MATERIAL_CONTACT_NULL_CAPACITY||n->rank!=n->dimension-1||n->nullity!=1||
       n->dimension!=report->active_count||!report->matrix_available||n->mode_index!=report->matrix_mode_index||
       n->factor_mode_index!=n->mode_index||n->raw_pair_mode_index!=n->mode_index||n->effective_pair_mode_index!=n->mode_index||
       n->pair_checks_mode_index!=n->mode_index||n->reaction_mode_index!=n->mode_index||n->interval_mode_index!=n->mode_index||
       n->selection_mode_index!=n->mode_index||n->constraint_count<1||n->constraint_count>DANGO_MATERIAL_CONTACT_NULL_CONSTRAINTS||
       report->mode_history_count<1||report->mode_history_count>DANGO_MATERIAL_CONTACT_BLOCK_SOLVES||
       report->matrix_mode_index!=report->mode_history_count-1) return false;
    for(int i=0;i<n->dimension;++i)
    {
        int id=report->active_row_ids[i];
        if(id<0||id>=DANGO_MATERIAL_CONTACT_ROWS||!reference->rows[id].active||n->original_row_ids[i]!=id||
           !isfinite(n->effective_lambda_particular[i])||!isfinite(n->effective_lambda_direction[i])||
           !isfinite(n->particular_residual[i])||!isfinite(n->particular_allowance[i])||
           !isfinite(n->direction_residual[i])||!isfinite(n->direction_allowance[i])||
           n->particular_allowance[i]<0||n->direction_allowance[i]<0||
           fabs(n->particular_residual[i])>n->particular_allowance[i]||fabs(n->direction_residual[i])>n->direction_allowance[i]||
           report->mode_history[report->mode_history_count-1][i]!=report->modes[i]) return false;
        for(int j=0;j<i;++j) if(report->active_row_ids[j]==id) return false;
    }
    for(int i=0;i<17;++i)
        if(!isfinite(n->reaction_residual[i])||!isfinite(n->reaction_allowance[i])||n->reaction_allowance[i]<0||
           fabs(n->reaction_residual[i])>n->reaction_allowance[i]) return false;
    bool positive=false;
    for(int i=0;i<n->constraint_count;++i)
    {
        const dango_material_contact_null_constraint* v=&n->constraints[i];
        double residual=v->a*n->selected_alpha+v->b;
        if(!isfinite(v->a)||!isfinite(v->b)||!isfinite(residual)) return false;
        if(residual>0&&v->row_id==n->empty_row_id) positive=true;
    }
    if(!positive) return false;
    c->trace->qualified=true;return true;
}
static inline bool dango_singleton_next_mode(const dango_material_contact_solver* reference,dango_material_contact_block_report* report)
{
    if(!dango_singleton_qualified(reference,report)) return false;
    dango_singleton_trace* t=dango_singleton_current.trace;
    // The attempt spends the shared budget permanently, even on later failure.
    dango_singleton_budget_current.spent=2;t->budget_kind=2;++t->mode_attempts;
    dango_material_contact_block_mode next=DANGO_MATERIAL_CONTACT_BLOCK_ENABLED;
    int change=dango_singleton_certified_particular_mode(reference,report,&next);
#ifdef DANGO_SINGLETON_TEST_MODE_RETURN
    DANGO_SINGLETON_TEST_MODE_RETURN(change,next,reference,report);
#endif
    if(memcmp(report->modes,t->original_failure.modes,sizeof report->modes)!=0||
       memcmp(&report->null_reaction,&dango_singleton_current.observed,sizeof report->null_reaction)!=0) return false;
    if(change<0||change>=report->active_count||next<DANGO_MATERIAL_CONTACT_BLOCK_ENABLED||next>DANGO_MATERIAL_CONTACT_BLOCK_ZERO||
       next==report->modes[change]||dango_mcb_trial_seen(report,change,next)) return false;
    int id=report->active_row_ids[change];
    if(id<0||id>=DANGO_MATERIAL_CONTACT_ROWS||!reference->rows[id].active||
       (id<144&&(next!=DANGO_MATERIAL_CONTACT_BLOCK_ENABLED&&next!=DANGO_MATERIAL_CONTACT_BLOCK_RELEASED))||
       (id>=144&&(next==DANGO_MATERIAL_CONTACT_BLOCK_ENABLED||next==DANGO_MATERIAL_CONTACT_BLOCK_RELEASED))) return false;
    t->next_mode_valid=true;t->changed_column=change;t->changed_row=id;t->previous_mode=report->modes[change];t->next_mode=next;
    report->modes[change]=next;dango_singleton_current.fresh_failure=false;
    return true;
}
#endif
