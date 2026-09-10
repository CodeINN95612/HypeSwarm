using System.Collections.Generic;
using HypeSwarm.Shared.Combat;

namespace HypeSwarm.Shared.Abilities
{
    /// <summary>
    /// Where an effect step finds things to hit.
    /// </summary>
    /// <remarks>
    /// <b>This interface exists so Phase 2 step 7 can replace how targets are found without touching
    /// a single effect step.</b> Today the implementation is a list scan over every registered
    /// combatant, which is correct and cheap at five champions and a handful of dummies. At two
    /// thousand enemies it is not, and the replacement is the spatial hash (§11) behind this same
    /// method.
    ///
    /// <para>No effect step may call <c>Physics.OverlapSphere</c> or anything like it. Physics queries
    /// against a horde are precisely the performance problem the architecture exists to avoid, and a
    /// step that reached for one would keep working right up until the density gate.</para>
    /// </remarks>
    public interface IAbilityWorld
    {
        /// <summary>
        /// Appends everything the query matches to the list, nearest first when the query caps how
        /// many it wants.
        /// </summary>
        /// <remarks>
        /// The list belongs to the caller and is not cleared here, so one cast can accumulate from
        /// several queries — and so the per-cast allocation is one list rather than one per step,
        /// which at horde density with every elite casting is a difference worth having.
        /// </remarks>
        /// <returns>How many were added.</returns>
        int FindTargets(in TargetQuery query, List<ICombatant> into);
    }
}
