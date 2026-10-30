/* Editor opt-in side walls. Uses the room solver's SPD projection metric,
   guarded shape-safe fallback and coupled non-associated Coulomb solve.
   The ordinary product and embedded room dispatch remain unchanged. */
#define PREVIEW_WALL_CLEARANCE 1e-4
#define PREVIEW_WALL_ACTIVATION 1e-3
#define PREVIEW_WALL_POSITION_LIMIT .01
typedef struct belt_preview_contact_row {double basis[BELT_B];belt_v n;double pn,pt;int ground;} belt_preview_contact_row;
static double belt_preview_wall_theta(const double* q,int right){
 double roots[64],theta=0,value=belt_eval(q,0,0).x;int n=belt_roots(q,0,1,0,roots);
 for(int i=0;i<n;++i){double x=belt_eval(q,roots[i],0).x;if(right?x>value:x<value){theta=roots[i];value=x;}}return theta;
}
static int belt_preview_project(belt_body* b,double theta,belt_v normal,double penetration){
 double direction[BELT_D],effective,trial[BELT_D];
 if(!isfinite(penetration)||!belt_contact_projection_direction(b,theta,normal,direction,&effective))return belt_solver_fail(b,10);
 double lambda=(penetration+2e-9)/effective,max_change=0;
 for(int j=0;j<BELT_D;++j){double delta=lambda*direction[j];if(!isfinite(delta))return belt_solver_fail(b,10);max_change=fmax(max_change,fabs(delta));}
 if(max_change>PREVIEW_WALL_POSITION_LIMIT)lambda*=PREVIEW_WALL_POSITION_LIMIT/max_change;
 for(int j=0;j<BELT_D;++j)trial[j]=b->q[j]+lambda*direction[j];
 if(!belt_guard_path(b,b->q,trial,3)||!belt_material_domain_valid(b,trial)){
  if(b->guard[9]==BELT_GUARD_BUDGET)return belt_solver_fail(b,8);
  memcpy(trial,b->q,sizeof trial);double shift=fmin(PREVIEW_WALL_POSITION_LIMIT,penetration+2e-9);
  trial[0]+=shift*normal.x;trial[1]+=shift*normal.y;trial[34]+=shift*normal.x;trial[35]+=shift*normal.y;max_change=shift;
  if(!belt_guard_path(b,b->q,trial,3)||!belt_material_domain_valid(b,trial))return belt_solver_fail(b,9);
 }
 double momentum_before[3];belt_momentum(b,momentum_before);
 double elastic=belt_elastic_energy(b),gravity=belt_gravity_energy(b);memcpy(b->q,trial,sizeof trial);
 double momentum_after[3];belt_momentum(b,momentum_after);b->diag[19]+=momentum_after[2]-momentum_before[2];
 b->diag[15]+=belt_elastic_energy(b)-elastic;b->diag[16]+=belt_gravity_energy(b)-gravity;b->diag[17]=fmax(b->diag[17],fmin(max_change,PREVIEW_WALL_POSITION_LIMIT));return 1;
}
static int belt_preview_contact_solve(belt_body* b){
 if(!b->solid_walls)return belt_contact_solve(b);
 double bounds[4],foot;belt_bounds(b->q,bounds,&foot);
 if(bounds[0]-b->left>PREVIEW_WALL_ACTIVATION&&b->right-bounds[2]>PREVIEW_WALL_ACTIVATION)return belt_contact_solve(b);
 for(int iteration=0;iteration<32;++iteration){
  belt_bounds(b->q,bounds,&foot);
  if(bounds[1]<b->cfg.ground_height-2e-9){if(!belt_preview_project(b,foot,(belt_v){0,1},b->cfg.ground_height-bounds[1]+PREVIEW_WALL_CLEARANCE))return 0;continue;}
  if(bounds[0]<b->left){if(!belt_preview_project(b,belt_preview_wall_theta(b->q,0),(belt_v){1,0},b->left-bounds[0]+PREVIEW_WALL_CLEARANCE))return 0;continue;}
  if(bounds[2]>b->right){if(!belt_preview_project(b,belt_preview_wall_theta(b->q,1),(belt_v){-1,0},bounds[2]-b->right+PREVIEW_WALL_CLEARANCE))return 0;continue;}
  break;
 }
 belt_bounds(b->q,bounds,&foot);
 if(bounds[1]<b->cfg.ground_height-2e-9||bounds[0]<b->left||bounds[2]>b->right)return belt_solver_fail(b,14);
 belt_preview_contact_row rows[192];belt_contact ground[64];int n=belt_contact_rows(b,ground,PREVIEW_WALL_ACTIVATION);
 b->grounded=n>0;b->stats[25]=n;
 for(int i=0;i<n;++i){memset(&rows[i],0,sizeof rows[i]);memcpy(rows[i].basis,ground[i].basis,sizeof rows[i].basis);rows[i].n=(belt_v){0,1};rows[i].ground=1;}
 double roots[64];int count=belt_roots(b->q,0,1,0,roots);
 for(int i=0;i<count&&n<192;++i){belt_v p=belt_eval(b->q,roots[i],0),dd=belt_eval(b->q,roots[i],2),normal;
  if(p.x-b->left<=PREVIEW_WALL_ACTIVATION&&dd.x>=-1e-9)normal=(belt_v){1,0};
  else if(b->right-p.x<=PREVIEW_WALL_ACTIVATION&&dd.x<=1e-9)normal=(belt_v){-1,0};else continue;
  belt_preview_contact_row* row=&rows[n++];memset(row,0,sizeof *row);belt_basis(roots[i],0,row->basis);row->n=normal;
 }
 double momentum_before[3];belt_momentum(b,momentum_before);
 double before=belt_kinetic(b),residual=0;int iteration;
 for(iteration=0;iteration<300;++iteration){residual=0;for(int i=0;i<n;++i){belt_preview_contact_row* r=&rows[i];double k=0,vn=0,vt=0;belt_v t={-r->n.y,r->n.x};
   for(int j=0;j<BELT_B;++j){double weight=r->basis[j];k+=weight*weight/b->mass[2*j];vn+=weight*(r->n.x*b->v[2*j]+r->n.y*b->v[2*j+1]);vt+=weight*(t.x*b->v[2*j]+t.y*b->v[2*j+1]);}
   double next_n=fmax(0,r->pn-vn/k),next_t=fmax(-b->cfg.friction*next_n,fmin(b->cfg.friction*next_n,r->pt-vt/k)),dn=next_n-r->pn,dt=next_t-r->pt;
   residual=fmax(residual,k*hypot(dn,dt));r->pn=next_n;r->pt=next_t;
   for(int j=0;j<BELT_B;++j){double weight=r->basis[j]/b->mass[2*j];b->v[2*j]+=weight*(r->n.x*dn+t.x*dt);b->v[2*j+1]+=weight*(r->n.y*dn+t.y*dt);}}
  if(residual<2e-8)break;
 }
 b->solver_diag[12]=residual;if(residual>2e-6)return belt_solver_fail(b,11);
 double normal=0,tangent=0;for(int i=0;i<n;++i){normal+=rows[i].pn;tangent+=fabs(rows[i].pt);}
 double after=belt_kinetic(b);b->stats[26]+=before;b->stats[27]+=after;b->stats[28]+=normal;b->stats[29]+=tangent;b->stats[30]=fmax(b->stats[30],residual);b->stats[52]+=after-before;
 b->diag[12]+=before;b->diag[13]+=after;b->diag[14]+=after-before;
 double momentum_after[3];belt_momentum(b,momentum_after);for(int j=0;j<3;++j)b->diag[35+j]+=momentum_after[j]-momentum_before[j];
 if(!(after<=before+1e-9*(1+before)))return belt_solver_fail(b,12);return 1;
}
