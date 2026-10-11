#define _CRT_SECURE_NO_WARNINGS
#include <stdio.h>
#include <stdlib.h>
static int injected_return,injected_final;
#define DANGO_SINGLETON_TEST_MODE_RETURN(change,next,reference,report) do { \
    if(injected_return==1) change=-1; \
    if(injected_return==2) change=(report)->active_count; \
    if(injected_return==3) next=(dango_material_contact_block_mode)99; \
    if(injected_return==4) next=(report)->modes[change]; \
    if(injected_return==5) next=DANGO_MATERIAL_CONTACT_BLOCK_STICK; \
    if(injected_return==6) (report)->modes[0]=DANGO_MATERIAL_CONTACT_BLOCK_RELEASED; \
} while(0)
#define DANGO_SINGLETON_TEST_BEFORE_FINAL(b) do{if(injected_final)(b)->contact.velocities[0].x+=.001;}while(0)
#include "candidate.c"
static unsigned assertions,failures,negatives;
static dango_material_contact_diagnostic captured,healthy;
static unsigned char raw[262144];
static dango_material_contact_block block,baseline;
static dango_material_contact_block_report report,baseline_report;
static dango_singleton_trace trace;
static dango_endpoint_trace endpoint;
static dango_constraint_state output,baseline_output,limited,sentinel;
static dango_material_contact_solver rebuilt;
static dango_material_contact_report built,double_report,float_report;
static dango_material_contact_cap_report cap;
static dango_material_contact_null_report neighbor_fixture;
static void check(bool ok,int line,const char* text)
{ ++assertions;if(!ok){++failures;fprintf(stderr,"FAIL %d %s\n",line,text);} }
#define CHECK(x) check((x),__LINE__,#x)
#define NEG(x) do{++negatives;CHECK(!(x));}while(0)
static bool load(const char* path,dango_material_contact_diagnostic* value)
{
 FILE* f=fopen(path,"rb");if(!f)return false;size_t n=fread(raw,1,sizeof raw,f);int extra=fgetc(f);fclose(f);
 if(n!=sizeof raw||extra!=EOF)return false;memset(value,0,sizeof *value);memcpy(value,raw,157832);return true;
}
static void normalize_null(dango_material_contact_null_report* n)
{
 n->operator_certificate_elapsed_seconds=0;n->operator_certificate_time_available=false;
 dango_material_contact_operator_certificate* cs[]={&n->particular_certificate,&n->direction_certificate,&n->reaction_certificate,&n->selected_certificate};
 for(int i=0;i<4;++i){cs[i]->elapsed_seconds=0;cs[i]->time_available=false;}
}
static void normalize(dango_material_contact_block_report* r)
{r->elapsed_seconds=0;r->time_available=false;r->operator_certificate_elapsed_seconds=0;r->operator_certificate_time_available=false;normalize_null(&r->null_reaction);}
static void build_third(void)
{
 CHECK(dango_material_contact_block_build(&captured.damped,captured.receipts,captured.receipt_count,captured.block.contact.step_seconds,&block,&report));
 CHECK(dango_singleton_begin(&trace,&endpoint,&block));
}
static void prepare_failure(void)
{
 build_third();block.attempted=true;CHECK(dango_mcb_record_modes(&block.report));++block.report.linear_solves;
 CHECK(dango_mcb_system(&dango_singleton_current.reference,&block.report));
 NEG(dango_mcb_null_solve(&dango_singleton_current.reference,&block.report));
 CHECK(block.report.null_reaction.status==6&&block.report.null_reaction.empty_row_id==142&&dango_singleton_current.fresh_failure);
}
static void negative_tests(void)
{
 // A gate may reject after begin but before the physical block was attempted.
 // The successful build's default status cannot count as a successful solve.
 build_third();CHECK(!block.attempted&&block.report.status==DANGO_MATERIAL_CONTACT_BLOCK_OK);
 dango_singleton_end(false);NEG(trace.block_passed);
 CHECK(!trace.adapter_passed&&!trace.substep_passed&&!trace.snapshot_published&&trace.mode_attempts==0&&trace.budget_kind==0);
#define BAD(code) do{prepare_failure();code;NEG(dango_singleton_next_mode(&dango_singleton_current.reference,&block.report));CHECK(trace.mode_attempts==0);dango_singleton_end(false);}while(0)
 BAD(dango_singleton_current.fresh_failure=false);
 BAD(dango_singleton_current.observed.status=DANGO_MATERIAL_CONTACT_NULL_OK);
 BAD(block.built=false);BAD(block.attempted=false);
 BAD(block.contact.geometry.velocities[0].x+=1);
 BAD(dango_endpoint_current.report=&captured.block_report.null_reaction);
 BAD(dango_endpoint_current.trace=NULL);
 BAD(dango_singleton_budget_current.owner=NULL);
 BAD(block.report.null_reaction.mode_index++);
 BAD(block.report.null_reaction.selected_alpha=0);
 BAD(block.report.null_reaction.lower-=1);
 BAD(block.report.active_row_ids[0]++);
 BAD(block.report.matrix_mode_index++);
 BAD(block.report.mode_history_count=0);
 BAD(block.report.modes[0]=DANGO_MATERIAL_CONTACT_BLOCK_RELEASED);
 BAD(dango_singleton_budget_current.spent=1);
 BAD(dango_singleton_budget_current.spent=2);
 BAD(endpoint.neighbor_attempts=1);
#undef BAD
 // Even a matching observation payload cannot bypass the semantic guards.
#define FORGED(code) do{prepare_failure();code;dango_singleton_current.observed=block.report.null_reaction;NEG(dango_singleton_next_mode(&dango_singleton_current.reference,&block.report));CHECK(trace.mode_attempts==0);dango_singleton_end(false);}while(0)
 FORGED(block.report.null_reaction.status=DANGO_MATERIAL_CONTACT_NULL_REACTION);
 FORGED(block.report.null_reaction.used=true);FORGED(block.report.null_reaction.selected_pair_available=true);
 FORGED(block.report.null_reaction.selected_checks_passed=true);FORGED(block.report.null_reaction.selection_available=false);
 FORGED(block.report.null_reaction.selection_passed=true);FORGED(block.report.null_reaction.lower_bounded=false);
 FORGED(block.report.null_reaction.upper_bounded=false);FORGED(block.report.null_reaction.upper+=1);
 FORGED(block.report.null_reaction.lower=NAN);FORGED(block.report.null_reaction.upper=INFINITY);
 FORGED(block.report.null_reaction.factor_available=false);FORGED(block.report.null_reaction.factor_passed=false);
 FORGED(block.report.null_reaction.raw_pair_available=false);FORGED(block.report.null_reaction.effective_pair_available=false);
 FORGED(block.report.null_reaction.pair_checks_available=false);FORGED(block.report.null_reaction.pair_checks_passed=false);
 FORGED(block.report.null_reaction.reaction_checks_available=false);FORGED(block.report.null_reaction.reaction_checks_passed=false);
 FORGED(block.report.null_reaction.interval_available=false);FORGED(block.report.null_reaction.interval_passed=false);
 FORGED(block.report.null_reaction.factor_mode_index++);FORGED(block.report.null_reaction.raw_pair_mode_index++);
 FORGED(block.report.null_reaction.effective_pair_mode_index++);FORGED(block.report.null_reaction.pair_checks_mode_index++);
 FORGED(block.report.null_reaction.reaction_mode_index++);FORGED(block.report.null_reaction.interval_mode_index++);
 FORGED(block.report.null_reaction.selection_mode_index++);FORGED(block.report.null_reaction.rank=6);
 FORGED(block.report.null_reaction.nullity=0);FORGED(block.report.null_reaction.dimension=5);
 FORGED(block.report.null_reaction.particular_residual[0]=1);FORGED(block.report.null_reaction.direction_residual[0]=1);
 FORGED(block.report.null_reaction.reaction_residual[0]=1);FORGED(block.report.null_reaction.particular_allowance[0]=-1);
 FORGED(block.report.null_reaction.direction_allowance[0]=NAN);FORGED(block.report.null_reaction.reaction_allowance[0]=INFINITY);
 FORGED(block.report.null_reaction.constraint_count=0);FORGED(block.report.null_reaction.constraints[4].b=0);
 FORGED(block.report.null_reaction.constraints[0].a=NAN);
#undef FORGED
 prepare_failure();baseline_report=block.report;
 NEG(dango_singleton_next_mode(&dango_singleton_current.reference,&baseline_report));CHECK(trace.mode_attempts==0);dango_singleton_end(false);
 for(int i=1;i<=6;++i)
 {
  prepare_failure();injected_return=i;NEG(dango_singleton_next_mode(&dango_singleton_current.reference,&block.report));injected_return=0;
  CHECK(trace.mode_attempts==1&&dango_singleton_budget_current.spent==2);NEG(dango_singleton_neighbor_available());
  NEG(dango_singleton_next_mode(&dango_singleton_current.reference,&block.report));CHECK(trace.mode_attempts==1);dango_singleton_end(false);
 }
 prepare_failure();block.report.mode_history_count=2;block.report.matrix_mode_index=1;
 // A valid-looking repeated next mode is still rejected, not another priority search.
 block.report.null_reaction.mode_index=1;block.report.null_reaction.factor_mode_index=1;block.report.null_reaction.raw_pair_mode_index=1;
 block.report.null_reaction.effective_pair_mode_index=1;block.report.null_reaction.pair_checks_mode_index=1;block.report.null_reaction.reaction_mode_index=1;
 block.report.null_reaction.interval_mode_index=1;block.report.null_reaction.selection_mode_index=1;
 memcpy(block.report.mode_history[1],block.report.modes,sizeof block.report.modes);block.report.mode_history[0][3]=DANGO_MATERIAL_CONTACT_BLOCK_RELEASED;
 dango_singleton_current.observed=block.report.null_reaction;
 NEG(dango_singleton_next_mode(&dango_singleton_current.reference,&block.report));dango_singleton_end(false);
 build_third();NEG(dango_singleton_begin(&trace,&endpoint,&block));dango_singleton_end(false);
 // Actual neighbor attempt followed by the real singleton selection failure.
 prepare_failure();dango_material_contact_null_report third=block.report.null_reaction;
 block.report.null_reaction=neighbor_fixture;block.report.null_reaction.status=DANGO_MATERIAL_CONTACT_NULL_OK;
 CHECK(dango_mcn_interval_select(&block.report.null_reaction));CHECK(endpoint.neighbor_attempts==1&&dango_singleton_budget_current.spent==1);
 block.report.null_reaction=third;block.report.null_reaction.status=DANGO_MATERIAL_CONTACT_NULL_OK;
 NEG(dango_mcn_interval_select(&block.report.null_reaction));
 NEG(dango_singleton_next_mode(&dango_singleton_current.reference,&block.report));CHECK(trace.mode_attempts==0&&endpoint.neighbor_attempts==1);dango_singleton_end(false);
 // Actual singleton attempt followed by the approved neighbor fixture.
 prepare_failure();CHECK(dango_singleton_next_mode(&dango_singleton_current.reference,&block.report));
 block.report.null_reaction=neighbor_fixture;block.report.null_reaction.status=DANGO_MATERIAL_CONTACT_NULL_OK;
 NEG(dango_mcn_interval_select(&block.report.null_reaction));CHECK(endpoint.neighbor_attempts==0&&trace.mode_attempts==1&&dango_singleton_budget_current.spent==2);dango_singleton_end(false);
 // A real failure after the mode rebuild must keep both the budget and output.
 build_third();memset(&sentinel,0x5A,sizeof sentinel);output=sentinel;injected_final=1;
 NEG(dango_material_contact_block_solve(&block,&output,&report));injected_final=0;dango_singleton_end(false);
 CHECK(block.attempted&&block.report.status!=DANGO_MATERIAL_CONTACT_BLOCK_OK);
 CHECK(trace.mode_attempts==1&&trace.budget_kind==2&&!trace.block_passed&&!trace.adapter_passed&&memcmp(&output,&sentinel,sizeof output)==0);
}
static void fixed_positive(void)
{
 CHECK(dango_material_contact_block_build(&captured.damped,captured.receipts,captured.receipt_count,captured.block.contact.step_seconds,&baseline,&baseline_report));
 memset(&sentinel,0x5A,sizeof sentinel);baseline_output=sentinel;
 NEG(dango_material_contact_block_solve(&baseline,&baseline_output,&baseline_report));CHECK(memcmp(&baseline_output,&sentinel,sizeof sentinel)==0);
 CHECK(baseline_report.null_reaction.status==6&&baseline_report.null_reaction.empty_row_id==142);
 build_third();CHECK(dango_material_contact_block_solve(&block,&output,&report));dango_singleton_end(true);
 CHECK(block.attempted&&block.report.status==DANGO_MATERIAL_CONTACT_BLOCK_OK&&trace.block_passed&&trace.adapter_passed);
 CHECK(trace.mode_attempts==1&&trace.budget_kind==2&&trace.changed_row==142&&trace.previous_mode==0&&trace.next_mode==1&&trace.next_mode_valid);
 CHECK(endpoint.neighbor_attempts==0&&trace.original_failure.null_reaction.status==6&&trace.block_passed);
 CHECK(report.linear_solves==2&&report.full_checks==1&&report.float_checks==1&&block.contact.rows[142].active&&block.contact.rows[59].active);
 dango_material_contact_block_report original=trace.original_failure;normalize(&original);normalize(&baseline_report);CHECK(memcmp(&original,&baseline_report,sizeof original)==0);
 CHECK(dango_material_contact_build(&captured.damped,captured.receipts,captured.receipt_count,captured.block.contact.step_seconds,&rebuilt,&built));
 double_report=built;CHECK(dango_mc_inspect_rows(&block.contact,&rebuilt,block.contact.velocities,&double_report));
 CHECK(dango_mc_inspect_ledger(&block.contact,&rebuilt,block.contact.velocities,false,&double_report));
 CHECK(dango_material_contact_validate(&block.contact,&output,&float_report));
 float maximum=0;for(int i=0;i<17;++i)maximum=fmaxf(maximum,b2Length(output.velocities[i]));
 float scale=maximum>12?(12*.99999f)/maximum:1;limited=output;
 for(int i=0;i<17;++i)limited.velocities[i]=(b2Vec2){(float)(block.contact.velocities[i].x*(double)scale),(float)(block.contact.velocities[i].y*(double)scale)};
 CHECK(dango_material_contact_block_validate_after_double_scale(&block,&output,&limited,scale,12,&cap));
 dango_constraint_state bad=output;bad.velocities[0].x+=.001f;dango_material_contact_report rejected;
 NEG(dango_material_contact_validate(&block.contact,&bad,&rejected));
 bad=limited;bad.velocities[0].x+=.001f;dango_material_contact_cap_report rejected_cap;
 NEG(dango_material_contact_block_validate_after_double_scale(&block,&output,&bad,scale,12,&rejected_cap));
 double lambda=block.contact.rows[142].multiplier;block.contact.rows[142].multiplier+=.001;
 NEG(dango_material_contact_validate(&block.contact,&output,&rejected));block.contact.rows[142].multiplier=lambda;
}
static void ordinary(void)
{
 CHECK(dango_material_contact_block_build(&healthy.damped,healthy.receipts,healthy.receipt_count,healthy.block.contact.step_seconds,&baseline,&baseline_report));
 CHECK(dango_material_contact_block_solve(&baseline,&baseline_output,&baseline_report));
 CHECK(dango_material_contact_block_build(&healthy.damped,healthy.receipts,healthy.receipt_count,healthy.block.contact.step_seconds,&block,&report));
 CHECK(dango_singleton_begin(&trace,&endpoint,&block));CHECK(dango_material_contact_block_solve(&block,&output,&report));dango_singleton_end(true);
 CHECK(!trace.original_failure_available&&trace.mode_attempts==0&&endpoint.neighbor_attempts==0);
 CHECK(memcmp(&block.contact,&baseline.contact,sizeof block.contact)==0&&memcmp(&output,&baseline_output,sizeof output)==0);
 normalize(&report);normalize(&baseline_report);CHECK(memcmp(&report,&baseline_report,sizeof report)==0);
}
int main(int argc,char** argv)
{
 if(argc!=5)return 2;CHECK(offsetof(dango_material_contact_diagnostic,singleton)==157832);
 CHECK(load(argv[1],&captured));CHECK(load(argv[2],&healthy));if(failures)return 1;
 FILE* neighbor=fopen(argv[4],"rb");if(!neighbor)return 2;
 unsigned char prior[138720];CHECK(fread(prior,1,sizeof prior,neighbor)==sizeof prior&&fgetc(neighbor)==EOF);fclose(neighbor);
 memcpy(&neighbor_fixture,prior+95912+offsetof(dango_material_contact_block_report,null_reaction),sizeof neighbor_fixture);
 negative_tests();ordinary();fixed_positive();
 FILE* f=fopen(argv[3],"wbx");if(!f)return 2;
 CHECK(fwrite(&block,sizeof block,1,f)==1);CHECK(fwrite(&output,sizeof output,1,f)==1);CHECK(fwrite(&limited,sizeof limited,1,f)==1);
 CHECK(fwrite(&trace,sizeof trace,1,f)==1);CHECK(fclose(f)==0);
 printf("{\"kind\":\"singleton-fixed-negative\",\"assertions\":%u,\"negativeCases\":%u,\"failures\":%u,\"diagnosticBytes\":%zu,\"traceBytes\":%zu,\"changedRow\":%d,\"modeAttempts\":%d,\"budgetKind\":%d,\"solves\":%d,\"doubleLedger\":%s,\"floatLedger\":%s,\"cap\":%s,\"capScale\":%.17g}\n",assertions,negatives,failures,sizeof captured,sizeof trace,trace.changed_row,trace.mode_attempts,trace.budget_kind,block.report.linear_solves,double_report.ledger_evaluated?"true":"false",float_report.ledger_evaluated?"true":"false",cap.status==0?"true":"false",(double)cap.scale);
 return failures?1:0;
}
