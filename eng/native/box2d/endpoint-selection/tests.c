#define _CRT_SECURE_NO_WARNINGS
#include <stdio.h>
#include <stdlib.h>
#include <float.h>
#include "candidate.c"

static unsigned assertions, failures, negatives;
static dango_material_contact_diagnostic captured, healthy;
static dango_material_contact_block block, baseline;
static dango_material_contact_block_report report, baseline_report;
static dango_endpoint_trace trace;
static dango_material_contact_null_report frozen, trial, expected, mirrored;
static dango_constraint_state output, baseline_output, limited;
static unsigned char raw[138720];
static void assertion(bool value,int line,const char* expression)
{ ++assertions; if(!value) { ++failures; fprintf(stderr,"FAIL %d %s\n",line,expression); } }
#define CHECK(value) assertion((value),__LINE__,#value)
#define NEGATIVE(value) do { ++negatives; CHECK(!(value)); } while(0)

static bool load(const char* path,dango_material_contact_diagnostic* diagnostic)
{
    FILE* f=fopen(path,"rb"); if(!f) return false;
    size_t bytes=fread(raw,1,sizeof raw,f); int end=fgetc(f); fclose(f);
    if(bytes!=sizeof raw || end!=EOF) return false;
    memset(diagnostic,0,sizeof(*diagnostic)); memcpy(diagnostic,raw,sizeof raw); return true;
}
static void reset_trial(void)
{ trial=frozen; trial.status=DANGO_MATERIAL_CONTACT_NULL_OK; }
static bool select_trial(void)
{
    if(!dango_endpoint_begin(&trace,&trial)) return false;
    bool passed=dango_mcn_interval_select(&trial); dango_endpoint_end(); return passed;
}
static void normalize_null_time(dango_material_contact_null_report* n)
{
    n->operator_certificate_elapsed_seconds=0; n->operator_certificate_time_available=false;
    dango_material_contact_operator_certificate* certificates[]={&n->particular_certificate,&n->direction_certificate,&n->reaction_certificate,&n->selected_certificate};
    for(int i=0;i<4;++i) { certificates[i]->elapsed_seconds=0; certificates[i]->time_available=false; }
}
static void normalize_time(dango_material_contact_block_report* r)
{
    r->time_available=false; r->elapsed_seconds=0;
    r->operator_certificate_elapsed_seconds=0; r->operator_certificate_time_available=false;
    normalize_null_time(&r->null_reaction);
}
static void scalar_tests(void)
{
    reset_trial(); expected=trial; CHECK(!dango_endpoint_original_select(&expected));
    CHECK(expected.status==DANGO_MATERIAL_CONTACT_NULL_INTERVAL_EMPTY && expected.empty_row_id==132);
    CHECK(select_trial()); CHECK(trace.neighbor_attempts==1 && trace.original_failure_available && trace.neighbor_constraints_passed);
    CHECK(memcmp(&expected,&trace.original_failure,sizeof expected)==0);
    CHECK(trial.selected_alpha==nextafter(frozen.lower,INFINITY));
    double multipliers[32],solution[32];
    CHECK(dango_mcn_selected(6,captured.block.report.scaling,captured.block.report.scaled_matrix,captured.block.report.scaled_rhs,&trial,multipliers,solution));
    CHECK(trial.selected_checks_passed);
    // Mirror the same exact constraint/effective-direction pair to an upper endpoint.
    reset_trial(); mirrored=trial;
    trial.lower=-mirrored.upper; trial.upper=-mirrored.lower;
    for(int i=0;i<trial.constraint_count;++i) trial.constraints[i].a=-trial.constraints[i].a;
    for(int i=0;i<trial.dimension;++i) {
        trial.effective_lambda_direction[i]=-trial.effective_lambda_direction[i];
        trial.effective_z_direction[i]=-trial.effective_z_direction[i];
    }
    CHECK(select_trial()); CHECK(trace.neighbor_attempts==1 && trial.selected_alpha==nextafter(-frozen.lower,-INFINITY));
    CHECK(dango_mcn_selected(6,captured.block.report.scaling,captured.block.report.scaled_matrix,captured.block.report.scaled_rhs,&trial,multipliers,solution));
    CHECK(trial.selected_checks_passed);
    // Ordinary successful selection: the complete unchanged report is identical.
    reset_trial(); trial.constraints[3].b=nextafter(trial.constraints[3].b,-INFINITY); expected=trial;
    CHECK(dango_endpoint_original_select(&expected)); CHECK(select_trial());
    CHECK(trace.neighbor_attempts==0 && !trace.original_failure_available);
    CHECK(memcmp(&expected,&trial,sizeof trial)==0);
#define BAD(code) do { reset_trial(); code; NEGATIVE(select_trial()); CHECK(!trace.neighbor_constraints_passed); } while(0)
    BAD(trial.status=DANGO_MATERIAL_CONTACT_NULL_REACTION);
    BAD(trial.status=DANGO_MATERIAL_CONTACT_NULL_INTERVAL_EMPTY);
    BAD(trial.factor_available=false); BAD(trial.factor_passed=false); BAD(trial.raw_pair_available=false);
    BAD(trial.effective_pair_available=false); BAD(trial.pair_checks_available=false); BAD(trial.pair_checks_passed=false);
    BAD(trial.reaction_checks_available=false); BAD(trial.reaction_checks_passed=false); BAD(trial.interval_available=false);
    BAD(trial.interval_passed=false); BAD(trial.mode_index=-1); BAD(trial.factor_mode_index++); BAD(trial.raw_pair_mode_index++);
    BAD(trial.effective_pair_mode_index++); BAD(trial.pair_checks_mode_index++); BAD(trial.reaction_mode_index++); BAD(trial.interval_mode_index++);
    BAD(trial.rank=trial.dimension); BAD(trial.nullity=0);
    BAD(trial.particular_residual[0]=1); BAD(trial.direction_residual[0]=1); BAD(trial.reaction_residual[0]=1);
    BAD(trial.particular_allowance[0]=-1); BAD(trial.direction_allowance[0]=NAN); BAD(trial.reaction_allowance[0]=INFINITY);
    BAD(trial.upper=trial.lower); BAD(trial.lower=nextafter(trial.upper,INFINITY));
    BAD(trial.lower=NAN); BAD(trial.upper=INFINITY); BAD(trial.constraints[0].a=NAN);
    BAD(trial.lower=-1;trial.upper=1;trial.constraints[3].a=0;trial.constraints[3].b=1);
    BAD(trial.constraints[3].b*=2);
    BAD(trial.constraint_count=1;trial.constraints[0].a=0;trial.constraints[0].b=1;trial.lower=DBL_MAX;trial.upper_bounded=false);
    BAD(trial.upper=trial.lower;trial.lower_bounded=false;trial.constraints[3].a=0;trial.constraints[3].b=1);
#undef BAD
    reset_trial(); CHECK(dango_endpoint_begin(&trace,&trial));
    expected=trial; NEGATIVE(dango_mcn_interval_select(&expected)); CHECK(trace.neighbor_attempts==0); dango_endpoint_end();
    reset_trial(); CHECK(dango_endpoint_begin(&trace,&trial)); CHECK(dango_mcn_interval_select(&trial));
    reset_trial(); NEGATIVE(dango_mcn_interval_select(&trial)); CHECK(trace.neighbor_attempts==1); dango_endpoint_end();
    reset_trial(); CHECK(dango_endpoint_begin(&trace,&trial)); NEGATIVE(dango_endpoint_begin(&trace,&trial)); dango_endpoint_end();
}
static void fixed_tests(void)
{
    double dt=captured.block.contact.step_seconds;
    CHECK(dango_material_contact_block_build(&captured.damped,captured.receipts,3,dt,&baseline,&baseline_report));
    NEGATIVE(dango_material_contact_block_solve(&baseline,&baseline_output,&baseline_report));
    CHECK(baseline_report.null_reaction.status==6 && baseline_report.null_reaction.empty_row_id==132);
    CHECK(dango_material_contact_block_build(&captured.damped,captured.receipts,3,dt,&block,&report));
    CHECK(dango_endpoint_begin(&trace,&block.report.null_reaction));
    CHECK(dango_material_contact_block_solve(&block,&output,&report)); dango_endpoint_end();
    CHECK(trace.neighbor_attempts==1 && trace.original_failure.status==6 && trace.original_failure.empty_row_id==132);
    expected=trace.original_failure; mirrored=baseline_report.null_reaction;
    normalize_null_time(&expected); normalize_null_time(&mirrored);
    CHECK(memcmp(&expected,&mirrored,sizeof expected)==0);
    dango_material_contact_report validated;
    CHECK(dango_material_contact_validate(&block.contact,&output,&validated));
    float maximum=0; for(int i=0;i<17;++i) maximum=fmaxf(maximum,b2Length(output.velocities[i]));
    float scale=maximum>12?(12*.99999f)/maximum:1;
    limited=output;
    for(int i=0;i<17;++i) limited.velocities[i]=(b2Vec2){(float)(block.contact.velocities[i].x*(double)scale),(float)(block.contact.velocities[i].y*(double)scale)};
    dango_material_contact_cap_report cap;
    CHECK(dango_material_contact_block_validate_after_double_scale(&block,&output,&limited,scale,12,&cap));
    dango_constraint_state corrupt=output; corrupt.velocities[0].x+=.001f;
    NEGATIVE(dango_material_contact_validate(&block.contact,&corrupt,&validated));
    corrupt=limited; corrupt.velocities[0].x+=.001f;
    NEGATIVE(dango_material_contact_block_validate_after_double_scale(&block,&output,&corrupt,scale,12,&cap));
    double saved=block.contact.rows[132].multiplier; block.contact.rows[132].multiplier+=.001;
    NEGATIVE(dango_material_contact_validate(&block.contact,&output,&validated)); block.contact.rows[132].multiplier=saved;
    // Last healthy actual input is an ordinary full solve, compared field by field
    // via complete struct bits after excluding observational timing only.
    CHECK(dango_material_contact_block_build(&healthy.damped,healthy.receipts,healthy.receipt_count,healthy.block.contact.step_seconds,&baseline,&baseline_report));
    CHECK(dango_material_contact_block_solve(&baseline,&baseline_output,&baseline_report));
    CHECK(dango_material_contact_block_build(&healthy.damped,healthy.receipts,healthy.receipt_count,healthy.block.contact.step_seconds,&block,&report));
    CHECK(dango_endpoint_begin(&trace,&block.report.null_reaction));
    CHECK(dango_material_contact_block_solve(&block,&output,&report)); dango_endpoint_end();
    CHECK(trace.neighbor_attempts==0 && !trace.original_failure_available);
    CHECK(memcmp(&baseline.contact,&block.contact,sizeof block.contact)==0);
    CHECK(memcmp(&baseline_output,&output,sizeof output)==0);
    normalize_time(&baseline_report); normalize_time(&report);
    CHECK(memcmp(&baseline_report,&report,sizeof report)==0);
}
int main(int argc,char** argv)
{
    if(argc!=3) return 2;
    CHECK(offsetof(dango_material_contact_diagnostic,block)==30008);
    CHECK(offsetof(dango_material_contact_diagnostic,block_report)==95912);
    CHECK(offsetof(dango_material_contact_diagnostic,endpoint)==138720);
    CHECK(load(argv[1],&captured)); CHECK(load(argv[2],&healthy));
    if(failures) return 1;
    frozen=captured.block_report.null_reaction;
    scalar_tests(); fixed_tests();
    printf("{\"kind\":\"endpoint-fixed-negative\",\"assertions\":%u,\"negativeCases\":%u,\"failures\":%u,\"diagnosticBytes\":%zu,\"traceBytes\":%zu,\"oldPrefixBytes\":138720,\"stepSeconds\":%.17g}\n",assertions,negatives,failures,sizeof captured,sizeof trace,captured.block.contact.step_seconds);
    return failures?1:0;
}
