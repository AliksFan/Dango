// Frozen solver-boundary fixtures are populated only after the natural replay
// has reproduced both approved red baselines. This placeholder deliberately
// contains no fabricated Box2D checkpoint data.
#ifndef DANGO_JOINT_CONTACT_FIXTURES_H
#define DANGO_JOINT_CONTACT_FIXTURES_H

#define DANGO_JOINT_FIXTURE_SCHEMA_VERSION 1

typedef struct dango_joint_fixture_record
{
    unsigned int schema_version;
    unsigned int target_frame;
    unsigned int target_substep;
    unsigned int reserved;
    dango_joint_state state;
} dango_joint_fixture_record;

#endif
