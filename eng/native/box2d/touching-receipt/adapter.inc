static dango_rh_capture* dango_tra_source(void* value)
{dango_self_soft_body* body=value;return body?dango_rh_get(body->world_id):NULL;}
static bool dango_tra_receipts(void* value,dango_material_ground_receipt* raw,int* count)
{
    if(!value || !raw || !count)return false;
    dango_material_contact_diagnostic fresh={0};
    if(!dango_mca_receipts(value,&fresh))return false;
    *count=fresh.receipt_count;memcpy(raw,fresh.receipts,(size_t)*count*sizeof *raw);return true;
}
static bool dango_rha_check(dango_self_soft_body* body,const dango_rh_view* v,const dango_constraint_state* state,const dango_material_contact_solver* solver)
{
    (void)v;
    return dango_tr_current.body==body && dango_tr_context_check(state,solver);
}
static void dango_rha_capture_rejected(dango_material_contact_diagnostic* d)
{
    unsigned long long step=d->substeps+1;memset(d,0,sizeof *d);d->substeps=step;
    d->error=-96;d->history_phase=1;d->history_status=3;d->report_valid=true;
    dango_mcb_report_init(&d->block_report);d->block_report.status=DANGO_MATERIAL_CONTACT_BLOCK_BUILD;
    d->block_report.final_rows.status=DANGO_MATERIAL_CONTACT_RECEIPT;d->report=d->block_report.final_rows;
}
static bool dango_rha_build(dango_self_soft_body* body,dango_material_contact_diagnostic* d,const dango_constraint_state* state,double dt)
{
#ifdef DANGO_RH_TEST_BEFORE_BUILD
    DANGO_RH_TEST_BEFORE_BUILD(body,d,state,dt);
    // Test observers may run a nested counterfactual adapter. Its binding never
    // becomes authority for the actual adapter that follows this callback.
    dango_tr_end();
#endif
    dango_rh_capture* source=dango_rh_get(body->world_id);
    d->history_phase=2;d->history_status=1;
    if(!dango_rh_raw_valid(d->receipts,d->receipt_count))d->history_status=2;
    else if(!dango_tr_filter(source,state,d->receipts,d->receipt_count,&d->history,&d->touching))d->history_status=3;
    else if(!dango_tr_begin(body,dango_tra_source,dango_tra_receipts,&d->history,&d->touching) || !dango_rha_check(body,&d->history,state,NULL))d->history_status=4;
    if(d->history_status!=1){
        dango_mcb_report_init(&d->block_report);d->block_report.status=DANGO_MATERIAL_CONTACT_BLOCK_BUILD;
        d->block_report.final_rows.status=DANGO_MATERIAL_CONTACT_RECEIPT;return false;
    }
    return dango_material_contact_block_build(state,d->history.current,d->history.current_count,dt,&d->block,&d->block_report);
}
static bool dango_rha_gate(dango_self_soft_body* body,dango_material_contact_diagnostic* d,const dango_constraint_state* state,int phase)
{
#ifdef DANGO_TR_TEST_BEFORE_GATE
    DANGO_TR_TEST_BEFORE_GATE(body,d,state,phase);
#endif
    d->history_phase=phase;
    if(!dango_rha_check(body,&d->history,state,&d->block.contact)){d->history_status=4;return false;}
    d->history_status=1;return true;
}
static bool dango_rha_solve(dango_self_soft_body* body,dango_material_contact_diagnostic* d)
{
    if(!dango_rha_gate(body,d,&d->damped,3))return false;
    if(!dango_material_contact_block_solve(&d->block,&d->uncapped,&d->block_report))return false;
    if(!dango_rha_gate(body,d,&d->uncapped,4))return false;
    dango_material_contact_solver reference;dango_material_contact_report r;
    if(!dango_material_contact_build(&d->damped,d->history.current,d->history.current_count,d->block.contact.step_seconds,&reference,&r))return false;
    if(!dango_mc_inspect_rows(&d->block.contact,&reference,d->block.contact.velocities,&r) || !dango_mc_inspect_ledger(&d->block.contact,&reference,d->block.contact.velocities,false,&r))return false;
    if(!dango_rha_gate(body,d,&d->uncapped,5))return false;
    return dango_material_contact_validate(&d->block.contact,&d->uncapped,&r);
}
