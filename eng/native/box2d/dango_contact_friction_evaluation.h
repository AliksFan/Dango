// Isolated node-edge velocity friction. No positional pin or persistent cache.
static float contact_friction_apply(b2Vec2* velocities,const float* inverse_mass,
    const float* weights,b2Vec2 tangent,float normal_impulse,float mu)
{
    if (mu<=0 || normal_impulse<=0) return 0;
    float speed=0,denominator=0;
    for (int i=0;i<3;++i)
    {
        speed+=weights[i]*b2Dot(velocities[i],tangent);
        denominator+=inverse_mass[i]*weights[i]*weights[i];
    }
    float limit=mu*normal_impulse;
    float impulse=fmaxf(-limit,fminf(limit,-speed/denominator));
    for (int i=0;i<3;++i)
        velocities[i]=b2MulAdd(velocities[i],impulse*inverse_mass[i]*weights[i],tangent);
    return impulse;
}
