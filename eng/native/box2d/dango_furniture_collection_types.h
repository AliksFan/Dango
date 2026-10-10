#ifndef DANGO_FURNITURE_COLLECTION_TYPES_H
#define DANGO_FURNITURE_COLLECTION_TYPES_H

#define DANGO_FURNITURE_COLLECTION_MAX 30
#define DANGO_FURNITURE_COLLECTION_ABI 1

/* Platform p coordinates; ball q=diag(4/3,1)p. No display units. */
typedef struct dango_furniture_definition
{
    int kind,reserved;
    dango_static_platform platform;
    int padding;
    dango_ball_definition ball;
} dango_furniture_definition;
typedef struct dango_furniture_collection_item
{
    uint64_t instance;
    dango_furniture_definition definition;
    dango_ball_state ball;
    dango_ball_contact dango_contact,surface_contact;
} dango_furniture_collection_item;
typedef struct dango_furniture_collection_frame
{
    dango_furniture_frame bodies;
    int item_count,reserved;
    dango_furniture_collection_item items[DANGO_FURNITURE_COLLECTION_MAX];
} dango_furniture_collection_frame;
typedef struct dango_furniture_collection_probe
{
    uint64_t world_generation,scene_revision,physics_step;
    double minimum_gap;
    int rejection,reserved;
} dango_furniture_collection_probe;

#endif
