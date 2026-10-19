#ifndef DANGO_BELT_PROFILE_H
#define DANGO_BELT_PROFILE_H
/* Independent, thread-local observation only. Buckets are inclusive: do not sum
   nested buckets as total frame time. No state participates in body rollback. */
#if defined(DANGO_BELT_PROFILE) && DANGO_BELT_PROFILE
#include <string.h>
#if defined(_WIN32)
#include <windows.h>
/* RPC headers define small as a legacy type macro. Keep the existing solver
   loop identifier intact in this observation-only build. */
#ifdef small
#undef small
#endif
#define BELT_PROFILE_TLS __declspec(thread)
#else
#include <time.h>
#define BELT_PROFILE_TLS _Thread_local
#endif
enum {
 BELT_PROFILE_STEP_TOTAL, BELT_PROFILE_INTEGRATE, BELT_PROFILE_RESIDUALS,
 BELT_PROFILE_OBJECTIVE_MATRIX, BELT_PROFILE_OBJECTIVE_VALUE,
 BELT_PROFILE_GUARD_NEWTON, BELT_PROFILE_GUARD_INTEGRATED,
 BELT_PROFILE_GUARD_GROUND, BELT_PROFILE_GUARD_INITIAL,
 BELT_PROFILE_CONTACT_TOTAL, BELT_PROFILE_BOUNDS, BELT_PROFILE_ROOTS,
 BELT_PROFILE_CHOLESKY, BELT_PROFILE_SPEED_CERTIFICATE,
 BELT_PROFILE_FRAME_STATS, BELT_PROFILE_FORCES, BELT_PROFILE_BUCKET_COUNT
};
static BELT_PROFILE_TLS double belt_profile_ms[BELT_PROFILE_BUCKET_COUNT];
static BELT_PROFILE_TLS double belt_profile_calls[BELT_PROFILE_BUCKET_COUNT];
static inline double belt_profile_now(void){
#if defined(_WIN32)
 static BELT_PROFILE_TLS double milliseconds_per_tick;
 LARGE_INTEGER ticks;
 if(milliseconds_per_tick==0){LARGE_INTEGER frequency;QueryPerformanceFrequency(&frequency);milliseconds_per_tick=1000.0/(double)frequency.QuadPart;}
 QueryPerformanceCounter(&ticks);return (double)ticks.QuadPart*milliseconds_per_tick;
#else
 return (double)clock()*1000.0/(double)CLOCKS_PER_SEC;
#endif
}
static inline void belt_profile_finish(int bucket,double start){
 double elapsed=belt_profile_now()-start;
 belt_profile_ms[bucket]+=elapsed;belt_profile_calls[bucket]+=1;
}
static inline void belt_profile_reset(void){
 memset(belt_profile_ms,0,sizeof belt_profile_ms);memset(belt_profile_calls,0,sizeof belt_profile_calls);
}
static inline void belt_profile_read(double* output){
 double result[48]={0};result[0]=1;result[1]=BELT_PROFILE_BUCKET_COUNT;
 for(int i=0;i<BELT_PROFILE_BUCKET_COUNT;++i){result[2+i]=belt_profile_ms[i];result[18+i]=belt_profile_calls[i];}
 memcpy(output,result,sizeof result);
}
#endif
#endif
