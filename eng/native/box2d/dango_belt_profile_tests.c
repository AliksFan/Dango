/* Profile-only ABI contracts; no production configuration or gates change. */
#ifndef DANGO_BELT_PROFILE
#define DANGO_BELT_PROFILE 1
#endif
#include "dango_belt.c"
#include <stdio.h>
#define CHECK(c) do{if(!(c)){fprintf(stderr,"FAIL line %d: %s\n",__LINE__,#c);return 1;}}while(0)
/* Same algebra/interface material fixture as the guard tests, with a small
   horizontal force to exercise the ordinary integration and force buckets. */
static dango_belt_config fixture={.node_count=16,.substep_count=4,.radial_rest_length=.68f,
 .radial_hertz=1.82f,.perimeter_hertz=15.075f,.radial_damping=1.85f,.perimeter_damping=1.85f,
 .area_strength=4.8f,.local_angle_balance_strength=1.7f,.core_mass=.12f,.node_mass=.015f,
 .ground_height=-100,.node_radius=.045f,.friction=.72f,.maximum_horizontal_speed=3.2f,
 .horizontal_force=.1f,.air_control_ratio=1,.maximum_speed=12};
static void fill_sentinel(double* output,int count){for(int i=0;i<count;++i)output[i]=123456.75+i;}
static int read_contract(void* handle,belt_body* body){
 unsigned char before[sizeof(belt_body)];memcpy(before,body,sizeof before);
 double output[52],sentinel[52],again[48];fill_sentinel(output,52);memcpy(sentinel,output,sizeof output);
 void* invalid=(void*)(uintptr_t)0x7fffffff;
 CHECK(dango_belt_reset_profile(NULL)==-601);
 CHECK(dango_belt_reset_profile(invalid)==-601);
 CHECK(dango_belt_read_profile(NULL,output,52)==-601);
 CHECK(dango_belt_read_profile(invalid,output,52)==-601);
 CHECK(dango_belt_read_profile(handle,NULL,48)==-600);
 CHECK(dango_belt_read_profile(handle,output,47)==-600);
 CHECK(dango_belt_read_profile(handle,output,-1)==-600);
 CHECK(memcmp(output,sentinel,sizeof output)==0);
 CHECK(memcmp(before,body,sizeof before)==0);
 CHECK(dango_belt_read_profile(handle,output,52)==0);
 CHECK(output[0]==1&&output[1]==16);
 CHECK(memcmp(output+48,sentinel+48,4*sizeof(double))==0);
 for(int i=2;i<34;++i)CHECK(isfinite(output[i])&&output[i]>=0);
 for(int i=34;i<48;++i)CHECK(output[i]==0);
 CHECK(dango_belt_read_profile(handle,again,48)==0);
 CHECK(memcmp(output,again,sizeof again)==0);
 CHECK(memcmp(before,body,sizeof before)==0);
 return 0;
}
static int reset_contract(void* handle,belt_body* body){
 unsigned char before[sizeof(belt_body)];memcpy(before,body,sizeof before);
 CHECK(dango_belt_reset_profile(handle)==0);
 CHECK(memcmp(before,body,sizeof before)==0);
 double output[48];CHECK(dango_belt_read_profile(handle,output,48)==0);
 CHECK(output[0]==1&&output[1]==16);
 for(int i=2;i<48;++i)CHECK(output[i]==0);
 CHECK(memcmp(before,body,sizeof before)==0);
 return 0;
}
int main(void){
 void* handle=NULL;CHECK(dango_belt_create(&fixture,0,0,1,&handle)==0);
 CHECK(dango_belt_set_shape_guard(handle,1)==0);belt_body* body=belt_lookup(handle);
 CHECK(read_contract(handle,body)==0);CHECK(reset_contract(handle,body)==0);
 /* An early API rejection must still finish the outer observation wrapper. */
 unsigned char before[sizeof(belt_body)];memcpy(before,body,sizeof before);
 float controls[9]={.25f,0,0,0,0,0,0,0,0};double output[48];
 CHECK(dango_belt_step(handle,0,controls,9)==-600);
 CHECK(memcmp(before,body,sizeof before)==0);
 CHECK(dango_belt_read_profile(handle,output,48)==0);
 CHECK(output[18+BELT_PROFILE_STEP_TOTAL]==1);
 CHECK(read_contract(handle,body)==0);
 CHECK(dango_belt_step(handle,1.0f/60,controls,9)==0);
 CHECK(dango_belt_read_profile(handle,output,48)==0);
 CHECK(output[18+BELT_PROFILE_STEP_TOTAL]==2);
 const int active[]={BELT_PROFILE_STEP_TOTAL,BELT_PROFILE_INTEGRATE,BELT_PROFILE_RESIDUALS,
  BELT_PROFILE_OBJECTIVE_MATRIX,BELT_PROFILE_OBJECTIVE_VALUE,BELT_PROFILE_GUARD_NEWTON,
  BELT_PROFILE_GUARD_INTEGRATED,BELT_PROFILE_GUARD_INITIAL,BELT_PROFILE_CONTACT_TOTAL,
  BELT_PROFILE_BOUNDS,BELT_PROFILE_ROOTS,BELT_PROFILE_CHOLESKY,
  BELT_PROFILE_SPEED_CERTIFICATE,BELT_PROFILE_FRAME_STATS,BELT_PROFILE_FORCES};
 for(size_t i=0;i<sizeof active/sizeof active[0];++i){
  CHECK(isfinite(output[2+active[i]])&&output[2+active[i]]>0);
  CHECK(output[18+active[i]]>0);
 }
 CHECK(read_contract(handle,body)==0);
 /* A physical guard refusal records observations despite body rollback. */
 body->q[34]=body->q[0]+2*body->radius;
 CHECK(dango_belt_step(handle,1.0f/60,controls,9)==-605);
 CHECK(dango_belt_read_profile(handle,output,48)==0);
 CHECK(output[18+BELT_PROFILE_STEP_TOTAL]==3);
 CHECK(output[18+BELT_PROFILE_GUARD_INITIAL]==2);
 CHECK(read_contract(handle,body)==0);CHECK(reset_contract(handle,body)==0);
 CHECK(dango_belt_destroy(handle)==0);
 puts("profile ABI bounds, sentinels, full-body immutability, stable reads, success/failure counts and TLS reset PASS");return 0;
}
