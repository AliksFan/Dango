static bool dango_rha_check(dango_self_soft_body* body,const dango_rh_view* v,const dango_constraint_state* state,const dango_material_contact_solver* solver)
{
    dango_material_contact_diagnostic fresh={0};
    if(!dango_mca_receipts(body,&fresh)) return false;
    return dango_rh_revalidate(v,fresh.receipts,fresh.receipt_count,state,solver);
}
static void dango_rha_capture_rejected(dango_material_contact_diagnostic* d)
{
    unsigned long long step=d->substeps+1; memset(d,0,sizeof *d); d->substeps=step;
    d->error=-96; d->history_phase=1; d->history_status=3; d->report_valid=true;
    dango_mcb_report_init(&d->block_report); d->block_report.status=DANGO_MATERIAL_CONTACT_BLOCK_BUILD;
    d->block_report.final_rows.status=DANGO_MATERIAL_CONTACT_RECEIPT; d->report=d->block_report.final_rows;
}
static bool dango_rha_build(dango_self_soft_body* body,dango_material_contact_diagnostic* d,const dango_constraint_state* state,double dt)
{
#ifdef DANGO_RH_TEST_BEFORE_BUILD
    DANGO_RH_TEST_BEFORE_BUILD(body,d,state,dt);
#endif
    dango_rh_capture* source=dango_rh_get(body->world_id);
    d->history_phase=2; d->history_status=1;
    if(!dango_rh_raw_valid(d->receipts,d->receipt_count)) d->history_status=2;
    else if(!dango_rh_filter(source,state,d->receipts,d->receipt_count,&d->history)) d->history_status=3;
    else if(!dango_rha_check(body,&d->history,state,NULL)) d->history_status=4;
    if(d->history_status!=1) {
        dango_mcb_report_init(&d->block_report); d->block_report.status=DANGO_MATERIAL_CONTACT_BLOCK_BUILD;
        d->block_report.final_rows.status=DANGO_MATERIAL_CONTACT_RECEIPT;
        return false;
    }
    return dango_material_contact_block_build(state,d->history.current,d->history.current_count,dt,&d->block,&d->block_report);
}
static bool dango_rha_gate(dango_self_soft_body* body,dango_material_contact_diagnostic* d,const dango_constraint_state* state,int phase)
{
    d->history_phase=phase;
    if(!dango_rha_check(body,&d->history,state,&d->block.contact)) { d->history_status=4; return false; }
    d->history_status=1; return true;
}
static bool dango_rha_solve(dango_self_soft_body* body,dango_material_contact_diagnostic* d)
{
    if(!dango_rha_gate(body,d,&d->damped,3)) return false;
    if(!dango_material_contact_block_solve(&d->block,&d->uncapped,&d->block_report)) return false;
    // Certify live history again before fresh double and float adjudications.
    if(!dango_rha_gate(body,d,&d->uncapped,4)) return false;
    dango_material_contact_solver reference; dango_material_contact_report r;
    if(!dango_material_contact_build(&d->damped,d->history.current,d->history.current_count,d->block.contact.step_seconds,&reference,&r)) return false;
    if(!dango_mc_inspect_rows(&d->block.contact,&reference,d->block.contact.velocities,&r) || !dango_mc_inspect_ledger(&d->block.contact,&reference,d->block.contact.velocities,false,&r)) return false;
    if(!dango_rha_gate(body,d,&d->uncapped,5)) return false;
    return dango_material_contact_validate(&d->block.contact,&d->uncapped,&r);
}
