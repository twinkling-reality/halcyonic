// What the parts of a person's world share, set as globals by WorldView.
#ifndef HALCYONIC_WORLD_INCLUDED
#define HALCYONIC_WORLD_INCLUDED

float _WorldReveal;      // meters the world has opened outward from the person as it arrives

// How much of the world shows at a distance while it arrives: it opens outward from the person.
half WorldArrived(float distance)
{
    return 1.0 - smoothstep(_WorldReveal - 160.0, _WorldReveal - 40.0, distance);
}

#endif
