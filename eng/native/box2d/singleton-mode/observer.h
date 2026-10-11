#ifndef DANGO_SINGLETON_OBSERVER_H
#define DANGO_SINGLETON_OBSERVER_H
static inline bool dango_mcn_interval_select(dango_material_contact_null_report* report)
{
    bool fresh=report&&report->status==DANGO_MATERIAL_CONTACT_NULL_OK;
    bool result=dango_singleton_v5_select(report);
    if(dango_singleton_budget_current.owner&&dango_singleton_budget_current.observe)
        dango_singleton_budget_current.observe(dango_singleton_budget_current.owner,report,fresh,result);
    return result;
}
#endif
