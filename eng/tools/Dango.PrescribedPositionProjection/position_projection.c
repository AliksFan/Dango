#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <stdint.h>
#include <string.h>
#include <stdlib.h>
static void observe_original(const void* state,int stage,int iteration);
#define DANGO_CONSTRAINT_DIAGNOSTIC_OBSERVE(s,stage,iteration) observe_original(s,stage,iteration)
#include "dango_prescribed_position_projection.h"
_Static_assert(sizeof(dango_constraint_state)==372,"R0 same-build state layout");
static FILE* history;
static int old_iterations;

static void number(float value)
{
    if(isfinite(value)) fprintf(history,"%.9g",(double)value); else fputs("null",history);
}
static float record_state(const dango_constraint_state* source,int stage,int iteration)
{
    dango_constraint_state s=*source;
    float residuals[16][5],maximum=0;
    for(int n=0;n<16;++n) {
        residuals[n][0]=dango_constraint_distance_mode(&s,16,n,s.radial_length*s.radial_min,
            s.radial_length*s.radial_max,false,DANGO_CONSTRAINT_CORRECT_POSITION);
        residuals[n][1]=dango_constraint_distance_mode(&s,n,(n+1)%16,s.perimeter_length*s.perimeter_min,
            s.perimeter_length*s.perimeter_max,false,DANGO_CONSTRAINT_CORRECT_POSITION);
        residuals[n][2]=dango_constraint_angle_mode(&s,n,false,false,DANGO_CONSTRAINT_CORRECT_POSITION);
        residuals[n][3]=dango_constraint_angle_mode(&s,n,true,false,DANGO_CONSTRAINT_CORRECT_POSITION);
        residuals[n][4]=fmaxf(0,s.ground+s.radius-s.positions[n].y)/s.radial_length;
        for(int k=0;k<5;++k) maximum=fmaxf(maximum,residuals[n][k]);
    }
    fprintf(history,"{\"Stage\":%d,\"Iteration\":%d,\"MaximumResidual\":",stage,iteration); number(maximum);
    fputs(",\"Rows\":[",history);
    for(int n=0;n<16;++n) {
        fprintf(history,"%s[",n ? "," : "");
        for(int k=0;k<5;++k) { if(k) fputc(',',history); number(residuals[n][k]); }
        fputc(']',history);
    }
    fputs("],\"Positions\":[",history);
    for(int n=0;n<17;++n) {
        fprintf(history,"%s[",n ? "," : ""); number(s.positions[n].x);
        fputc(',',history); number(s.positions[n].y); fputc(']',history);
    }
    fputs("]}\n",history);
    return maximum;
}
static void observe_original(const void* state,int stage,int iteration)
{
    if(stage==1) old_iterations=iteration;
    (void)record_state(state,stage,iteration);
}
static bool read_state(const char* path,dango_constraint_state* state)
{
    FILE* file=NULL;
    if(fopen_s(&file,path,"rb")) return false;
    bool valid=fread(state,sizeof *state,1,file)==1 && fgetc(file)==EOF;
    fclose(file); return valid;
}
static bool finite_state(const dango_constraint_state* s)
{
    // Copy into floats; do not alias a struct through float pointers.
    float values[93]; memcpy(values,s,sizeof values);
    for(int i=0;i<93;++i) if(!isfinite(values[i])) return false;
    return true;
}
int main(int argc,char** argv)
{
    if(argc!=5) { fputs("entry.bin exit.bin variant output-prefix\n",stderr); return 2; }
    dango_constraint_state entry,last,state;
    if(!read_state(argv[1],&entry) || !read_state(argv[2],&last) || !finite_state(&entry) ||
        entry.inverse_mass[16]!=0) return 2;
    int variant=atoi(argv[3]); if(variant < -1 || variant>5) return 2;
    char path[2048];
    if(sprintf_s(path,sizeof path,"%s.history.jsonl",argv[4])<0 || fopen_s(&history,path,"wb")) return 2;
    state=entry;
    r1_stats stats={0}; stats.bound_valid=true; stats.failure_kind=-1; stats.failure_node=-1;
    LARGE_INTEGER start,end,frequency; QueryPerformanceFrequency(&frequency); QueryPerformanceCounter(&start);
    bool claimed=false,baseline_exact=false;
    if(variant==-1) {
        claimed=dango_constraint_solve(&state);
        stats.iterations=old_iterations;
        baseline_exact=!claimed && old_iterations==96 && memcmp(&state,&last,sizeof state)==0;
    } else {
        if(variant==4) state.positions[16].y+=0.05f;
        (void)record_state(&state,0,0);
        for(int iteration=0;iteration<96;++iteration) {
            stats.iterations=iteration+1;
            if(variant==5) {
                claimed=r1_radial(&state,0,0,&stats);
                (void)record_state(&state,1,iteration+1); break;
            }
            if(!r1_sweep(&state,iteration,variant,&stats)) {
                (void)record_state(&state,3,iteration+1); break;
            }
            float residual=record_state(&state,1,iteration+1);
            // Deliberate missing-gate variants must still face the external full oracle.
            if(variant>=1 && variant<=3) {
                residual=0;
                dango_constraint_state copy=state;
                for(int n=0;n<16;++n) {
                    if(variant!=2) residual=fmaxf(residual,dango_constraint_distance(&copy,16,n,
                        copy.radial_length*copy.radial_min,copy.radial_length*copy.radial_max,false));
                    residual=fmaxf(residual,dango_constraint_distance(&copy,n,(n+1)%16,
                        copy.perimeter_length*copy.perimeter_min,copy.perimeter_length*copy.perimeter_max,false));
                    if(variant!=3) for(int display=0;display<2;++display)
                        residual=fmaxf(residual,dango_constraint_angle(&copy,n,display!=0,false));
                    if(variant!=1) residual=fmaxf(residual,fmaxf(0,copy.ground+copy.radius-copy.positions[n].y)/copy.radial_length);
                }
            }
            if(residual<=DANGO_CONSTRAINT_TOLERANCE) { claimed=true; break; }
        }
    }
    QueryPerformanceCounter(&end);
    float maximum=record_state(&state,claimed ? 2 : 3,stats.iterations);
    bool finite=finite_state(&state),core_same=memcmp(&entry.positions[16],&state.positions[16],sizeof(b2Vec2))==0;
    bool velocities_same=memcmp(entry.velocities,state.velocities,sizeof entry.velocities)==0;
    bool configuration_same=memcmp(entry.inverse_mass,state.inverse_mass,
        sizeof entry.inverse_mass+8*sizeof(float))==0;
    bool gate=claimed && finite && core_same && velocities_same && configuration_same &&
        stats.bound_valid && maximum<=DANGO_CONSTRAINT_TOLERANCE && stats.iterations<=96;
    bool success=variant==-1 ? baseline_exact : gate;
    fclose(history);
    if(sprintf_s(path,sizeof path,"%s.state.bin",argv[4])<0) return 2;
    FILE* output=NULL;
    if(fopen_s(&output,path,"wb")) return 2;
    bool written=fwrite(&state,sizeof state,1,output)==1; fclose(output); if(!written) return 2;
    printf("{\"Variant\":%d,\"Success\":%s,\"ClaimedSuccess\":%s,\"OriginalExitBitwiseEqual\":%s,"
        "\"Iterations\":%d,\"MaximumResidual\":%.9g,\"Finite\":%s,\"CoreUnchanged\":%s,"
        "\"VelocitiesUnchanged\":%s,\"ConfigurationUnchanged\":%s,\"BoundValid\":%s,"
        "\"FailureKind\":%d,\"FailureNode\":%d,\"RadialCandidates\":%llu,\"AngleRows\":%llu,"
        "\"ActiveSets\":%llu,\"SingularSets\":%llu,\"Corrections\":%llu,"
        "\"MaximumRowMove\":%.17g,\"SolveMilliseconds\":%.17g,\"WorldsCreated\":0}\n",
        variant,success ? "true" : "false",claimed ? "true" : "false",baseline_exact ? "true" : "false",
        stats.iterations,(double)maximum,finite ? "true" : "false",core_same ? "true" : "false",
        velocities_same ? "true" : "false",configuration_same ? "true" : "false",stats.bound_valid ? "true" : "false",
        stats.failure_kind,stats.failure_node,stats.radial_candidates,stats.angle_rows,stats.active_sets,
        stats.singular_sets,stats.corrections,stats.maximum_row_move,
        (double)(end.QuadPart-start.QuadPart)*1000/(double)frequency.QuadPart);
    return success ? 0 : 1;
}
