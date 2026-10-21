static int collection_project_body_ball(room_world* w,belt_body* b,int slot,double theta,belt_v n,double depth){
 if(!EXPERIMENT_METRIC)return collection_mass_project_body_ball(w,b,slot,theta,n,depth);
 dango_belt_room_collection_item* a=&w->items[slot];double z[BELT_D],k,trial[BELT_D];
 if(!isfinite(depth)||!metric_direction(b,theta,n,z,&k))return room_refuse(w,DANGO_BELT_ROOM_REJECTED,1);
 k+=1/a->definition.ball.mass;double lambda=(depth+2e-9)/k,maximum=lambda/a->definition.ball.mass;
 for(int j=0;j<BELT_D;++j)maximum=fmax(maximum,fabs(lambda*z[j]));if(maximum>ROOM_POSITION_LIMIT)lambda*=ROOM_POSITION_LIMIT/maximum;
 for(int j=0;j<BELT_D;++j)trial[j]=b->q[j]-lambda*z[j];
 if(!belt_guard_path(b,b->q,trial,3)||!belt_material_domain_valid(b,trial))return room_refuse(w,DANGO_BELT_ROOM_REJECTED,1);
 double elastic=belt_elastic_energy(b),gravity=belt_gravity_energy(b),by=a->ball.y;memcpy(b->q,trial,sizeof trial);
 a->ball.x+=lambda/a->definition.ball.mass*n.x;a->ball.y+=lambda/a->definition.ball.mass*n.y;
 double de=belt_elastic_energy(b)-elastic,dg=belt_gravity_energy(b)-gravity+.5*a->definition.ball.mass*b->cfg.gravity*(a->ball.y-by);
 w->diag[12]+=de;w->diag[13]+=dg;w->diag[14]=fmax(w->diag[14],fmin(maximum,ROOM_POSITION_LIMIT));w->diag[15]+=fmax(0,de+dg);return 1;
}
