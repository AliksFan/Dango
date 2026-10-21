/* Use the existing default product room fixture and its actual platform/contact
   path; this is one representative contact, not a furniture matrix. */
#define DANGO_BELT_ROOM_TRACE 1
#define main inherited_room_contract_main
#include "dango_belt_room_tests.c"
#undef main
static int balanced_contact_contract(void){
 void* h=NULL;CHECK(create_room(8,0,1.5f,&h)==0);
 dango_belt_room_platform platform={1,0,0,3,.2,.72};CHECK(dango_belt_room_set_platform(h,&platform,0)==0);
 dango_belt_room_input input={.body_id=1};room_world* room=room_lookup(h);CHECK(room!=NULL);
 for(int i=0;i<120;++i)CHECK(dango_belt_room_step(h,.0166666f,&input,1)==0);
 CHECK(room->diag[18]==1&&room_body(room)->grounded);
 for(int key=0;key<2;++key){memset(input.controls,0,sizeof input.controls);input.controls[key?1:6]=1;
  for(int tick=0;tick<6;++tick){CHECK(dango_belt_room_step(h,.0166666f,&input,1)==0);belt_body* body=room_body(room);double upper=0,lower=0;
   for(int i=0;i<(int)body->force_ledger[4];++i){const double* row=body->force_ledger+8+6*i;CHECK(row[4]==2&&row[2]==0);if(row[3]>0)upper+=row[3];else lower+=row[3];}
   CHECK(upper>0&&lower<0&&fabs(upper+lower)<1e-12&&body->stats[19]==0);
   CHECK(body->guard[3]==0&&body->stats[34]<=2e-7&&body->stats[56]<=40&&body->solver_diag[6]<20);
   CHECK(room->diag[4]<=ROOM_GEOMETRY_BUDGET&&room->diag[9]<=ROOM_PENETRATION);
  }
 }
 memset(input.controls,0,sizeof input.controls);input.controls[0]=input.controls[1]=input.controls[6]=1;CHECK(dango_belt_room_step(h,.0166666f,&input,1)==0);
 belt_body* body=room_body(room);int horizontal=0;CHECK(body->stats[18]>.01&&body->ramp==0);
 for(int i=0;i<(int)body->force_ledger[4];++i){const double* row=body->force_ledger+8+6*i;CHECK(row[4]!=2);horizontal+=row[4]==5;}CHECK(horizontal==8);
 input.controls[8]=1;CHECK(dango_belt_room_step(h,.0166666f,&input,1)==0);CHECK(room_body(room)->force_ledger[4]==0);
 CHECK(dango_belt_room_destroy(h)==0);puts("default product room real platform contact, paired W/S shared solver, unchanged gates, vertical-only opposing keys and full cancellation PASS");return 0;
}
int main(void){CHECK(balanced_contact_contract()==0);return 0;}
