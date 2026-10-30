/* Deliberately invalid loader fixtures. Never link into a product target. */
#include <stdint.h>
#if defined(_WIN32)
#define ROOM_FIXTURE_API __declspec(dllexport)
#else
#define ROOM_FIXTURE_API __attribute__((visibility("default")))
#endif
#if !defined(DANGO_BELT_ROOM_IDENTITY_VARIANT) || DANGO_BELT_ROOM_IDENTITY_VARIANT < 1 || DANGO_BELT_ROOM_IDENTITY_VARIANT > 3
#error Select exactly one invalid room identity fixture variant (1..3).
#endif

ROOM_FIXTURE_API int dango_belt_room_version(void)
{
    return DANGO_BELT_ROOM_IDENTITY_VARIANT == 1 ? 2 : 1;
}
ROOM_FIXTURE_API int dango_belt_room_capabilities(void)
{
    return DANGO_BELT_ROOM_IDENTITY_VARIANT == 2 ? 11 : 15;
}
ROOM_FIXTURE_API int dango_belt_room_frame_size(void)
{
    return DANGO_BELT_ROOM_IDENTITY_VARIANT == 3 ? 4480 : 4472;
}
ROOM_FIXTURE_API const char* dango_belt_room_model(void) { return "continuous-belt-room-v1"; }
ROOM_FIXTURE_API const char* dango_belt_room_tuning(void) { return "C455-F-v1"; }
ROOM_FIXTURE_API const char* dango_belt_room_contact_model(void) { return "belt-solid-aabb-r1-v1"; }

/* No create/step/read exports: metadata must reject the DLL before resolving
   or calling an operational API. A fixture cannot manufacture a body. */
