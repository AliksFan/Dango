#ifndef DANGO_SINGLETON_BUDGET_H
#define DANGO_SINGLETON_BUDGET_H
// Only an active, scoped block binds this supplementary budget. The retained
// v5 endpoint fixture API still has its own one-neighbor lifetime.
typedef struct dango_singleton_budget
{
    void* owner;
    int spent; // 0 none, 1 actual neighbor attempt, 2 actual singleton mode attempt
    void (*observe)(void*,const dango_material_contact_null_report*,bool,bool);
} dango_singleton_budget;
#if defined(_MSC_VER)
static __declspec(thread) dango_singleton_budget dango_singleton_budget_current;
#else
static _Thread_local dango_singleton_budget dango_singleton_budget_current;
#endif
static inline bool dango_singleton_neighbor_available(void)
{ return !dango_singleton_budget_current.owner || dango_singleton_budget_current.spent==0; }
static inline void dango_singleton_neighbor_spent(void)
{ if(dango_singleton_budget_current.owner) dango_singleton_budget_current.spent=1; }
#endif
