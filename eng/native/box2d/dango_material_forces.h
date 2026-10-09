// Isolated material candidate math. Not linked into any production or sample DLL.
#ifndef DANGO_MATERIAL_FORCES_H
#define DANGO_MATERIAL_FORCES_H

#define DANGO_MATERIAL_MAX_NODES 64

typedef struct dango_material_vec2
{
    double x;
    double y;
} dango_material_vec2;

typedef enum dango_material_status
{
    DANGO_MATERIAL_OK = 0,
    DANGO_MATERIAL_INVALID = -1,
    DANGO_MATERIAL_NONFINITE = -2,
    DANGO_MATERIAL_DEGENERATE = -3
} dango_material_status;

// Input is a simple CCW polygon, 3..64 nodes. Coordinates and parameters must
// be finite; collapsed edges/area and self-intersections are rejected.
// All input/output storage must be disjoint (including scalar outputs).
// On failure energy is NAN and nonaliased,
// valid-sized force output is zero. Callers must propagate nonzero status.
// The fixed gradient r*sin(2*pi/n) normalizes force magnitude at the natural
// regular polygon. At a compressed regular radius R, force is old pressure
// times R/r. This fixed scale is not recomputed from current shape.
// Arithmetic overflow or an unrepresentable positive area/coefficient is a
// NONFINITE range failure, not a successful zero-force state.
int dango_material_area(const dango_material_vec2* points, int count,
    double target_area, double natural_radius, double strength,
    double* energy, dango_material_vec2* forces);

// E = .5*k*sum((t_next-t_i)^2), t = signed turning angle in (-pi,pi).
// The reversal branch cut (within 1e-10 radians of +/-pi) is rejected.
// These are raw neighboring turning differences, not wrapped differences.
int dango_material_bending(const dango_material_vec2* points, int count,
    double stiffness, double* energy, dango_material_vec2* forces);

// Scale the whole force group by min(1,maximum_force/max_node_force).
// This retains net force/torque closure, but is not promised to remain the
// gradient of the original energy. Groups of 1..64 and a zero cap are valid.
// The scalar scale output must be disjoint from the force group.
// On failure the input group is unchanged and scale is NAN.
int dango_material_cap(dango_material_vec2* forces, int count,
    double maximum_force, double* scale);

#endif
