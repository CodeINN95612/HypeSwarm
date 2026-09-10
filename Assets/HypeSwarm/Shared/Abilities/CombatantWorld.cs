using System.Collections.Generic;
using HypeSwarm.Shared.Combat;
using UnityEngine;

namespace HypeSwarm.Shared.Abilities
{
    /// <summary>
    /// Every combatant that currently exists, and a linear scan over them.
    /// </summary>
    /// <remarks>
    /// <b>This is the placeholder implementation of <see cref="IAbilityWorld"/> and it is meant to be
    /// replaced.</b> A scan over a list is the right amount of machinery for five champions and a few
    /// dummies: it is obviously correct, it allocates nothing, and it cannot be wrong in a way that is
    /// hard to see. At two thousand enemies it is quadratic against the number of casts and Phase 2
    /// step 7 replaces it with the spatial hash (§11) behind the same interface.
    ///
    /// <para>Entities register themselves, rather than the world finding them. A <c>FindObjectsOfType</c>
    /// sweep is the alternative and it is both slow and subtly wrong — it would pick up prefab
    /// instances mid-spawn and anything disabled.</para>
    ///
    /// <para><b>Statics survive play mode in this project</b>, which enters play without a domain
    /// reload, so a registry left alone would start the second Play of a session holding destroyed
    /// combatants from the first. The reset below is what prevents that, the same way
    /// <c>GameTuning</c> handles it.</para>
    /// </remarks>
    public sealed class CombatantWorld : IAbilityWorld
    {
        static CombatantWorld shared = new CombatantWorld();

        readonly List<ICombatant> combatants = new List<ICombatant>();

        /// <summary>Scratch space for the nearest-first sort, reused so a cast allocates nothing.</summary>
        readonly List<ICombatant> scratch = new List<ICombatant>();

        /// <summary>The one every spawned entity registers with.</summary>
        public static CombatantWorld Shared => shared;

        /// <summary>How many are registered. For the dev panel and for tests.</summary>
        public int Count => combatants.Count;

        /// <summary>Everything registered, in registration order.</summary>
        public IReadOnlyList<ICombatant> All => combatants;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlayMode() => shared = new CombatantWorld();

        public void Register(ICombatant combatant)
        {
            if (combatant == null || combatants.Contains(combatant))
            {
                return;
            }

            combatants.Add(combatant);
        }

        public void Unregister(ICombatant combatant)
        {
            if (combatant != null)
            {
                combatants.Remove(combatant);
            }
        }

        public void Clear()
        {
            combatants.Clear();
        }

        public int FindTargets(in TargetQuery query, List<ICombatant> into)
        {
            if (into == null)
            {
                return 0;
            }

            if (query.IsUnlimited)
            {
                var added = 0;

                for (var i = 0; i < combatants.Count; i++)
                {
                    if (query.Matches(combatants[i]))
                    {
                        into.Add(combatants[i]);
                        added++;
                    }
                }

                return added;
            }

            return FindNearest(query, into);
        }

        /// <summary>
        /// A capped query, resolved nearest-first.
        /// </summary>
        /// <remarks>
        /// A selection sort over the matches rather than a full sort of them. The cap is a handful —
        /// it comes from a step function over a stat — so <c>n</c> passes over the matches beats
        /// sorting everything in range, and it keeps the allocation at zero. The spatial hash will
        /// want the same shape, because it too will produce more candidates than the cap wants.
        /// </remarks>
        int FindNearest(in TargetQuery query, List<ICombatant> into)
        {
            scratch.Clear();

            for (var i = 0; i < combatants.Count; i++)
            {
                if (query.Matches(combatants[i]))
                {
                    scratch.Add(combatants[i]);
                }
            }

            var wanted = query.MaxTargets < scratch.Count ? query.MaxTargets : scratch.Count;

            for (var picked = 0; picked < wanted; picked++)
            {
                var best = picked;
                var bestDistance = query.DistanceTo(scratch[picked]);

                for (var i = picked + 1; i < scratch.Count; i++)
                {
                    var distance = query.DistanceTo(scratch[i]);

                    if (distance < bestDistance)
                    {
                        best = i;
                        bestDistance = distance;
                    }
                }

                (scratch[picked], scratch[best]) = (scratch[best], scratch[picked]);
                into.Add(scratch[picked]);
            }

            scratch.Clear();

            return wanted;
        }
    }
}
