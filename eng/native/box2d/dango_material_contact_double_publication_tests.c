// Pure fixed-state publication tests; no world, DLL, scale search or relaxed gate.
#define main dp_retained_block_main
#include "dango_material_contact_block_tests.c"
#undef main
#include "dango_material_contact_double_publication.h"
#include "dango_material_contact_frame3368.h"

#ifndef DANGO_MATERIAL_CONTACT_SINGLE_ROUND_PUBLICATION_CANDIDATE
#error Single-round candidate required
#endif
_Static_assert(DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY==16,"retained independent helper stride");
#define DP_CHECK(c) do { ++bc_assertions; if(!(c)) { fprintf(stderr,"FAIL double publication %d: %s\n",__LINE__,#c); return 1; } } while(0)

static int dp_fixture_bits(const dango_constraint_state* state)
{
    float values[93]={state->radial_length,state->perimeter_length,state->radial_min,state->radial_max,
        state->perimeter_min,state->perimeter_max,state->radius,state->ground};
    int index=8;
    for(int n=0;n<17;++n)
    {
        values[index++]=state->positions[n].x; values[index++]=state->positions[n].y;
        values[index++]=state->velocities[n].x; values[index++]=state->velocities[n].y;
        values[index++]=state->inverse_mass[n];
    }
    DP_CHECK(index==93);
    for(int i=0;i<93;++i)
    { uint32_t bits; memcpy(&bits,&values[i],sizeof bits); DP_CHECK(bits==dango_material_contact_frame3368_float_bits[i]); }
    return 0;
}

static void dp_limit(const dango_material_contact_block* block,const dango_constraint_state* input,
    dango_constraint_state* output,float scale)
{
    *output=*input;
    for(int n=0;n<17;++n) output->velocities[n]=(b2Vec2){
        (float)(block->contact.velocities[n].x*(double)scale),
        (float)(block->contact.velocities[n].y*(double)scale)};
}

static int dp_independent_cap(const dango_material_contact_block* block,
    const dango_constraint_state* before,const dango_constraint_state* after,
    const dango_material_contact_cap_report* cap,float maximum_speed)
{
    double v[17][2],e0=0,e1=0,mass_sum=0,change[2]={0},angular=0,impulse_scale=0,torque_scale=0;
    for(int n=0;n<17;++n) { v[n][0]=after->velocities[n].x; v[n][1]=after->velocities[n].y; }
    for(int row=0;row<160;++row)
    {
        double gap,rate=bc_boundary_rate(&block->contact.geometry,v,row,&gap);
        double tolerance=row>=64 && row<128 ? DANGO_CONSTRAINT_TOLERANCE : DANGO_CONSTRAINT_TOLERANCE*before->radial_length;
        bool active=gap<=tolerance;
        if(row>=144) active=active && block->contact.support[row-144].receipt_present;
        DP_CHECK(active==block->contact.rows[row].active);
        double lambda=block->contact.rows[row].multiplier;
        DP_CHECK(isfinite(gap+rate+lambda));
        if(!active) { DP_CHECK(lambda==0); continue; }
        double normalized=row>=64 && row<128 ? rate : rate/before->radial_length;
        if(row<144)
        {
            DP_CHECK(lambda>=0 && normalized>=-DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
            if(lambda>0) DP_CHECK(normalized<=DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
            int mode=bc_mode_index(&block->report,row); DP_CHECK(mode>=0);
            if(block->report.modes[mode]==DANGO_MATERIAL_CONTACT_BLOCK_ENABLED)
                DP_CHECK(fabs(normalized)<=DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
        }
        else
        {
            int node=row-144;
            const dango_material_contact_support* support=&block->contact.support[node];
            double bound=support->friction*(support->base_normal+block->contact.rows[128+node].multiplier)+support->receipt_cone_slack;
            double total=support->base_tangent+lambda,epsilon=32*DBL_EPSILON*fmax(bound,fabs(total));
            DP_CHECK(bound>=0 && fabs(total)<=bound+epsilon);
            if(bound>0)
            {
                if(total<=-bound+epsilon) DP_CHECK(normalized>=-DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
                else if(total>=bound-epsilon) DP_CHECK(normalized<=DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
                else DP_CHECK(fabs(normalized)<=DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
            }
        }
    }
    for(int n=0;n<17;++n)
    {
        double mass=1.0/before->inverse_mass[n],vx0=before->velocities[n].x,vy0=before->velocities[n].y;
        double vx1=after->velocities[n].x,vy1=after->velocities[n].y;
        double dx=mass*(vx1-vx0),dy=mass*(vy1-vy0);
        double x=(double)before->positions[n].x-before->positions[16].x,y=(double)before->positions[n].y-before->positions[16].y;
        mass_sum+=mass; e0+=.5*mass*(vx0*vx0+vy0*vy0); e1+=.5*mass*(vx1*vx1+vy1*vy1);
        change[0]+=dx; change[1]+=dy; angular+=x*dy-y*dx;
        double magnitude=mass*(hypot(vx0,vy0)+hypot(vx1,vy1))+hypot(dx,dy);
        impulse_scale+=magnitude; torque_scale+=hypot(x,y)*magnitude;
        DP_CHECK(b2Length(after->velocities[n])<=maximum_speed);
    }
    double length=before->radial_length;
    double floor=mass_sum*length*length*DANGO_MATERIAL_CONTACT_RATE_TOLERANCE*DANGO_MATERIAL_CONTACT_RATE_TOLERANCE;
    DP_CHECK(e1<=e0+8*FLT_EPSILON*fmax(e0,floor));
    DP_CHECK(fabs(e0-cap->energy_before)<=128*DBL_EPSILON*fmax(e0,floor));
    DP_CHECK(fabs(e1-cap->energy_after)<=128*DBL_EPSILON*fmax(e1,floor));
    DP_CHECK(hypot(change[0]-cap->cap_momentum_change.x,change[1]-cap->cap_momentum_change.y)<=
        128*DBL_EPSILON*fmax(impulse_scale,mass_sum*length*DANGO_MATERIAL_CONTACT_RATE_TOLERANCE));
    DP_CHECK(fabs(angular-cap->cap_angular_momentum_change)<=
        128*DBL_EPSILON*fmax(torque_scale,mass_sum*length*length*DANGO_MATERIAL_CONTACT_RATE_TOLERANCE));
    return 0;
}

static int dp_frame3368(void)
{
    dango_constraint_state input=dango_material_contact_frame3368_state(),saved=input,contact;
    DP_CHECK(dp_fixture_bits(&input)==0);
    dango_material_contact_block block;
    dango_material_contact_block_report report;
    DP_CHECK(dango_material_contact_block_build(&input,NULL,0,dango_material_contact_frame3368_step_seconds,&block,&report));
    DP_CHECK(dango_material_contact_block_solve(&block,&contact,&report));
    DP_CHECK(report.active_count==14 && report.mode_history_count==1 && report.float_checks==1);
    DP_CHECK(memcmp(&input,&saved,sizeof input)==0);
    for(int n=0;n<17;++n) for(int axis=0;axis<2;++axis)
    {
        float value=axis?contact.velocities[n].y:contact.velocities[n].x; uint32_t bits;
        double dvalue=axis?block.contact.velocities[n].y:block.contact.velocities[n].x; uint64_t dbits;
        memcpy(&bits,&value,sizeof bits); memcpy(&dbits,&dvalue,sizeof dbits);
        DP_CHECK(bits==dango_material_contact_frame3368_uncapped_bits[2*n+axis]);
        DP_CHECK(dbits==dango_material_contact_frame3368_double_bits[2*n+axis]);
    }
    DP_CHECK(bc_independent_final(&block,&contact,false)==0);
    DP_CHECK(bc_independent_final(&block,&contact,true)==0);
    float maximum=0;
    for(int n=0;n<17;++n) maximum=fmaxf(maximum,b2Length(contact.velocities[n]));
    const float scale=(12*.99999f)/maximum;
    DP_CHECK(maximum==12.0528964996337890625f && scale==0.995601356029510498046875f);
    dango_constraint_state legacy=contact,limited;
    for(int n=0;n<17;++n) legacy.velocities[n]=b2MulSV(scale,contact.velocities[n]);
    dp_limit(&block,&contact,&limited,scale);
    dango_material_contact_cap_report cap;
    DP_CHECK(!dango_material_contact_block_validate_after_uniform_scale(&block,&contact,&legacy,scale,12,&cap));
    DP_CHECK(cap.status==DANGO_MATERIAL_CONTACT_FINAL_RATE && cap.contact_output_checked && cap.exact_common_scale_checked);
    DP_CHECK(cap.final_rows.maximum_angular_closing>DANGO_MATERIAL_CONTACT_RATE_TOLERANCE); ++bc_negatives;
    dango_material_contact_block saved_block=block;
    dango_constraint_state saved_contact=contact,saved_limited=limited;
    DP_CHECK(dango_material_contact_block_validate_after_double_scale(&block,&contact,&limited,scale,12,&cap));
    DP_CHECK(cap.status==DANGO_MATERIAL_CONTACT_OK && cap.contact_output_checked && cap.exact_common_scale_checked);
    DP_CHECK(cap.final_rows.float_output_checked && !cap.final_rows.ledger_evaluated);
    DP_CHECK(dp_independent_cap(&block,&contact,&limited,&cap,12)==0); ++bc_positives;
    DP_CHECK(memcmp(&block,&saved_block,sizeof block)==0 && memcmp(&contact,&saved_contact,sizeof contact)==0 && memcmp(&limited,&saved_limited,sizeof limited)==0);
    printf("PASS frame3368: original float-times-float FINAL_RATE; single-round angular=%.17g max=%.9g; original independent physical/cap ledger\n",
        cap.final_rows.maximum_angular_closing,cap.maximum_after);
    for(int mutation=0;mutation<20;++mutation)
    {
        dango_material_contact_block corrupt=block;
        dango_constraint_state wrong_contact=contact,wrong_final=limited;
        float wrong_scale=scale,wrong_maximum=12;
        switch(mutation)
        {
            case 0: wrong_final.velocities[0].x=nextafterf(wrong_final.velocities[0].x,INFINITY); break;
            case 1: wrong_contact.velocities[0].x=nextafterf(wrong_contact.velocities[0].x,INFINITY); break;
            case 2: wrong_scale=nextafterf(scale,0); dp_limit(&block,&contact,&wrong_final,wrong_scale); break;
            case 3: wrong_scale=0; break;
            case 4: wrong_scale=NAN; break;
            case 5: wrong_scale=1.01f; break;
            case 6: wrong_maximum=0; break;
            case 7: wrong_maximum=NAN; break;
            case 8: wrong_final.positions[0].x+=.001f; break;
            case 9: corrupt.contact.velocities[0].x+=1e-8;
                DP_CHECK((float)corrupt.contact.velocities[0].x==contact.velocities[0].x); break;
            case 10: corrupt.contact.velocities[0].x=NAN; break;
            case 11: corrupt.contact.rows[5].multiplier=-1; break;
            case 12: corrupt.report.modes[0]=DANGO_MATERIAL_CONTACT_BLOCK_RELEASED; break;
            case 13: corrupt.report.modes[0]=(dango_material_contact_block_mode)99; break;
            case 14: corrupt.report.active_count--; break;
            case 15: corrupt.report.active_row_ids[0]=4; break;
            case 16: corrupt.contact.input_velocities[0].x+=.01; break;
            case 17: corrupt.built=false; break;
            case 18: corrupt.attempted=false; break;
            default: wrong_final=legacy; break;
        }
        dango_material_contact_block before_block=corrupt;
        dango_constraint_state before_contact=wrong_contact,before_final=wrong_final;
        memset(&cap,0x5a,sizeof cap);
        DP_CHECK(!dango_material_contact_block_validate_after_double_scale(&corrupt,&wrong_contact,&wrong_final,wrong_scale,wrong_maximum,&cap));
        DP_CHECK(cap.status!=DANGO_MATERIAL_CONTACT_OK);
        if(mutation==9) DP_CHECK(cap.status==DANGO_MATERIAL_CONTACT_LEDGER);
        DP_CHECK(memcmp(&corrupt,&before_block,sizeof corrupt)==0);
        DP_CHECK(memcmp(&wrong_contact,&before_contact,sizeof wrong_contact)==0 && memcmp(&wrong_final,&before_final,sizeof wrong_final)==0);
        ++bc_negatives;
    }
    DP_CHECK(!dango_material_contact_block_validate_after_double_scale(NULL,&contact,&limited,scale,12,&cap)); ++bc_negatives;
    DP_CHECK(!dango_material_contact_block_validate_after_double_scale(&block,NULL,&limited,scale,12,&cap)); ++bc_negatives;
    DP_CHECK(!dango_material_contact_block_validate_after_double_scale(&block,&contact,NULL,scale,12,&cap)); ++bc_negatives;
    DP_CHECK(!dango_material_contact_block_validate_after_double_scale(&block,&contact,&limited,scale,12,NULL)); ++bc_negatives;
    return 0;
}

static int dp_no_cap_and_mode_guard(void)
{
    dango_constraint_state state=bc_ring(),contact,limited;
    dango_material_contact_block block;
    DP_CHECK(bc_success(&state,NULL,0,1.0/600,&block,&contact)==0);
    dp_limit(&block,&contact,&limited,1);
    dango_material_contact_cap_report cap;
    DP_CHECK(dango_material_contact_block_validate_after_double_scale(&block,&contact,&limited,1,12,&cap));
    DP_CHECK(dp_independent_cap(&block,&contact,&limited,&cap,12)==0); ++bc_positives;
    const float length=.01f/16;
    state.radial_length=length; state.perimeter_length*=length; state.radius*=length; state.radial_min=.98f;
    for(int n=0;n<16;++n) state.positions[n]=b2MulSV(length,state.positions[n]);
    state.positions[2]=b2MulSV(state.radial_min,state.positions[2]);
    state.velocities[2]=(b2Vec2){10,0}; state.velocities[16]=(b2Vec2){2,8};
    DP_CHECK(bc_success(&state,NULL,0,1.0/600,&block,&contact)==0);
    const float scale=(1*.99999f)/10;
    dp_limit(&block,&contact,&limited,scale);
    DP_CHECK(!dango_material_contact_block_validate_after_double_scale(&block,&contact,&limited,scale,1,&cap));
    DP_CHECK(cap.status==DANGO_MATERIAL_CONTACT_COMPLEMENTARITY); ++bc_negatives;
    printf("PASS no-cap positive and retained zero-reaction enabled-mode cap rejection\n");
    return 0;
}

int main(void)
{
    if(dp_frame3368() || dp_no_cap_and_mode_guard()) return 1;
    printf("PASS double publication suite: %d assertions; %d positives; %d rejected negatives\n",bc_assertions,bc_positives,bc_negatives);
    return 0;
}
