"""Compose the idle-rest candidate from the accepted touching-receipt source copy.

Usage: python generate.py --base <touching generated-v2 copy> --output <new dir>
Only adapter/dango_collision_review.c changes; every edit is an exact, unique
replacement so a drifted base fails instead of patching the wrong place.
"""
import argparse, hashlib, json, pathlib, re, shutil

ap = argparse.ArgumentParser()
ap.add_argument('--base', required=True)
ap.add_argument('--output', required=True)
args = ap.parse_args()
base = pathlib.Path(args.base).resolve()
out = pathlib.Path(args.output).resolve()
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
assert not out.exists()
shutil.copytree(base, out)
name = 'adapter/dango_collision_review.c'
path = out / name
text = path.read_text(encoding='utf-8')
original = text


def replace(old, new):
    global text
    assert text.count(old) == 1, old[:80]
    text = text.replace(old, new, 1)


replace('#include "dango_display_contact.h"\n',
        '#include "dango_display_contact.h"\n#define DANGO_REST_SLEEP 1\n'
        '// Single body: every rest frame is skipped, so pause without Box2D sleep.\n'
        '#define DANGO_REST_FREEZE_ONLY 1\n#include "dango_rest_sleep.h"\n')
replace('} collision_review;\n', '    dango_rest_state rest;\n} collision_review;\n')

# Frame start: wake checks, then an exact no-op frame while the single body rests.
replace('''    if (!p->error)
    {
        ++p->singleton_frames;''', '''    bool rest_near[DANGO_PHYSICS_MAX_BODIES]={false},rest_supported[DANGO_PHYSICS_MAX_BODIES]={false};
    bool rest_frozen=false;
    if (!p->error)
    {
        dango_rest_check_wake(&p->rest,p->bodies,p->active,p->capacity,controls,rest_near,p->config.node_radius);
        rest_frozen=dango_rest_all(&p->rest,p->active,p->capacity);
    }
    if (!p->error && !rest_frozen)
    {
        ++p->singleton_frames;''')

# Frame end: evaluate entry after a published frame.
replace('''            if(p->touching_events && p->touching_last.singleton.frame_token==p->singleton_frames) p->touching_last.singleton.snapshot_published=true;
        }''', '''            if(p->touching_events && p->touching_last.singleton.frame_token==p->singleton_frames) p->touching_last.singleton.snapshot_published=true;
            for (int r=0;r<p->capacity;++r) if (p->active[r])
            {
                for (int n=0;n<16;++n)
                    if (b2Body_GetPosition(p->bodies[r]->node_body_ids[n]).y-p->config.node_radius<=p->config.ground_height+.035f) rest_supported[r]=true;
                if (collision_support_slot(p,r)>=0) rest_supported[r]=true;
            }
            dango_rest_frame_end(&p->rest,p->bodies,p->active,p->capacity,controls,rest_supported,rest_near,p->config.node_radius,dt);
        }''')
replace('''    memcpy(output,p->snapshot,(size_t)p->capacity*42*sizeof(float)); *contact_steps=p->contact_steps;
    return p->error;
}''', '''    if (rest_frozen) for (int r=0;r<p->capacity;++r) if (p->active[r]) p->rest.slot[r].rest_seconds+=dt;
    memcpy(output,p->snapshot,(size_t)p->capacity*42*sizeof(float)); *contact_steps=p->contact_steps;
    return p->error;
}''')

# Every world mutation wakes all; slot lifetime clears its rest state.
for api in ['dango_physics_world_add', 'dango_physics_world_remove', 'dango_physics_world_reset',
            'dango_physics_world_set_preview_jump_speed_scale', 'dango_physics_world_set_horizontal_bounds',
            'dango_physics_world_clear_horizontal_bounds', 'dango_physics_world_translate_horizontal']:
    m = re.search(r'DANGO_API int ' + api + r'\([^)]*\)\s*\{\n    collision_review\* p=handle;\n', text)
    assert m, api
    text = text[:m.end()] + '    if (p) p->rest.wake_all=true;\n' + text[m.end():]
replace('    p->active[slot]=1; p->entity_ids[slot]=p->next_entity_id++;\n',
        '    p->active[slot]=1; p->entity_ids[slot]=p->next_entity_id++;\n    dango_rest_clear(&p->rest,slot);\n')
replace('    dango_self_destroy_rig(p->bodies[slot]);\n    p->active[slot]=0; p->entity_ids[slot]=0; --p->count;\n    memset(p->snapshot+42*slot,0,42*sizeof(float));\n    return 0;',
        '    dango_self_destroy_rig(p->bodies[slot]);\n    p->active[slot]=0; p->entity_ids[slot]=0; --p->count;\n    dango_rest_clear(&p->rest,slot);\n    memset(p->snapshot+42*slot,0,42*sizeof(float));\n    return 0;')
replace('    if (!p) return -410;\n    dango_rh_end(&p->receipt_history);\n',
        '    if (!p) return -410;\n    for (int slot=0;slot<p->capacity;++slot) dango_rest_clear(&p->rest,slot);\n    dango_rh_end(&p->receipt_history);\n')
text += '''
// Read-only presentation/test hook: 1 for each resting entity, in slot order.
DANGO_API int dango_physics_world_get_rest_state(void* handle,int* entity_ids,int* resting,int capacity,int* count)
{
    collision_review* p=handle;
    if (!p || !entity_ids || !resting || !count || capacity<p->count) return -410;
    int output=0;
    for (int r=0;r<p->capacity;++r) if (p->active[r])
    { entity_ids[output]=p->entity_ids[r]; resting[output]=p->rest.slot[r].resting ? 1 : 0; ++output; }
    *count=output;
    return 0;
}
'''
path.write_text(text, encoding='utf-8', newline='\n')
manifest = {'kind': 'idle-rest-candidate-source-copy', 'changedFile': name,
            'baseSha256': hashlib.sha256(original.encode('utf-8')).hexdigest(), 'candidateSha256': sha(path),
            'touchingManifestSha256': sha(base / 'touching-source-manifest.json')}
(out / 'idle-rest-manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
print(json.dumps({'passed': True, **manifest}))
