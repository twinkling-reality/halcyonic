#nullable enable
using System;
using UnityEngine;

namespace Halcyonic.XR
{
    /// <summary>
    /// Something that knows a better place for the stage than its default in front of the person,
    /// for example a real surface found through scene understanding and kept with a spatial anchor.
    /// The stage uses <see cref="Preferred"/> while it is set and its own default otherwise.
    /// </summary>
    public interface IStagePlacementSource
    {
        /// <summary>Raised on the main thread when <see cref="Preferred"/> changes.</summary>
        event Action? Changed;

        /// <summary>The pose the stage should take, in world space, or null for the default.</summary>
        Pose? Preferred { get; }
    }
}
