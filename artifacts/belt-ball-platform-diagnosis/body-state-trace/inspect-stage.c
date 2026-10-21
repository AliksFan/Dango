#define main existing_test_main
#include "src/dango_belt_room_collection_tests.c"
#undef main
static void state_trace(const char* e,const belt_body* b,double t,double x,double y,double d){(void)e;(void)b;(void)t;(void)x;(void)y;(void)d;}
static int load(const char* path,room_world* w,belt_body* b){FILE* f=NULL;fopen_s(&f,path,"rb");if(!f)return 0;int ok=fread(w,sizeof *w,1,f)==1&&fread(b,sizeof *b,1,f)==1;fclose(f);return ok;}
static void run(const char* label,const belt_body* input){
 belt_body b=*input;double h=(double).0166666f/4,force[BELT_D]={0};force[1]=-b.mass[1]*b.cfg.gravity;force[35]=-b.mass[35]*b.cfg.gravity;
 double before=belt_elastic_energy(&b);int ok=belt_integrate(&b,h,force);
 printf("%s ok%d site%.0f residual%.17g iter%.0f elasticBefore%.17g elasticAfter%.17g guardWork%.0f\n",label,ok,b.solver_diag[3],b.solver_diag[7],b.solver_diag[5],before,belt_elastic_energy(&b),b.guard[4]);
}
int main(void){room_world w;belt_body actual,unprojected,projected;
 if(!load("state-024780-before_integrate.bin",&w,&actual)||!load("state-024767-static_before.bin",&w,&unprojected)||!load("state-024778-static_after.bin",&w,&projected))return 2;
 printf("flags radial%d whale%d substeps%d dt%.17g\n",actual.radial_drive,actual.whale_drive,actual.cfg.substep_count,(double).0166666f/4);
 run("actual-next-integrate",&actual);
 belt_body experiment=actual;memcpy(experiment.q,unprojected.q,sizeof experiment.q);run("same-velocity-unprojected-q-diagnostic-only",&experiment);
 run("before-position-stage-same-old-velocity",&unprojected);run("after-position-stage-same-old-velocity",&projected);
 return 0;
}
