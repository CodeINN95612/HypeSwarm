using System;
using System.Collections.Generic;
using HypeSwarm.Shared.Stats;

namespace HypeSwarm.Shared.Combat
{
    /// <summary>
    /// Absorbing health that sits in front of the real pool, in named layers that expire on their own.
    /// </summary>
    /// <remarks>
    /// Layers rather than one number, because a shield has to be removable by source the way a
    /// modifier is (§9) — dispelling one shield must not take the other two with it, and a shield
    /// that expires must take away only what is left of itself.
    ///
    /// <para><b>Damage drains the layer that expires soonest first.</b> The alternative orders are all
    /// worse: newest-first wastes an about-to-expire shield the player is standing in, and
    /// largest-first makes a small emergency shield useless. Soonest-first is also what a player
    /// assumes, which matters more than it sounds — a shield that failed to absorb something reads as
    /// a bug.</para>
    ///
    /// <para>Pure C#, and time is passed in rather than read, for the same reason
    /// <see cref="StatBuffs"/> does it: a schedule that reads a clock cannot be tested.</para>
    /// </remarks>
    public sealed class ShieldPool
    {
        struct Layer
        {
            public ModifierSource Source;
            public float Amount;
            public float Remaining;
        }

        readonly List<Layer> layers = new List<Layer>();

        float total;

        /// <summary>Anything about the shields changed — an add, a drain, an expiry, a removal.</summary>
        public event Action Changed;

        /// <summary>
        /// A layer ended with absorption left in it, by expiry or by being replaced. Carries what went
        /// unused, which is what a "your shield expired" readout wants.
        /// </summary>
        public event Action<ModifierSource, float> Expired;

        /// <summary>Absorption remaining across every layer.</summary>
        public float Total => total;

        public int Count => layers.Count;

        /// <summary>
        /// Adds a shield.
        /// </summary>
        /// <remarks>
        /// A second shield from the same source replaces the first rather than adding to it, matching
        /// <see cref="StatBuffs"/>: a re-cast shield should be a fresh shield, not a growing one that
        /// no single removal can take off. Stacking is a source per stack, which is what
        /// <see cref="ModifierSource.Instance"/> exists for.
        /// </remarks>
        /// <param name="duration">Seconds, or <see cref="float.PositiveInfinity"/> for one that only a removal ends.</param>
        /// <returns>False if the amount or the duration was not positive.</returns>
        public bool Add(ModifierSource source, float amount, float duration)
        {
            if (amount <= 0f || duration <= 0f || float.IsNaN(amount) || float.IsNaN(duration))
            {
                return false;
            }

            Remove(source);

            layers.Add(new Layer { Source = source, Amount = amount, Remaining = duration });
            total += amount;

            Changed?.Invoke();

            return true;
        }

        /// <summary>Ends a shield early. Dispelling, and how a re-cast clears the previous one.</summary>
        /// <returns>False if that source had no shield.</returns>
        public bool Remove(ModifierSource source)
        {
            for (var i = 0; i < layers.Count; i++)
            {
                if (layers[i].Source != source)
                {
                    continue;
                }

                var left = layers[i].Amount;

                layers.RemoveAt(i);
                total -= left;

                Announce(source, left);
                Changed?.Invoke();

                return true;
            }

            return false;
        }

        /// <summary>Drops every layer. For death, and for a run ending.</summary>
        public void Clear()
        {
            if (layers.Count == 0)
            {
                return;
            }

            var ended = layers.ToArray();

            layers.Clear();
            total = 0f;

            for (var i = 0; i < ended.Length; i++)
            {
                Announce(ended[i].Source, ended[i].Amount);
            }

            Changed?.Invoke();
        }

        /// <summary>Absorption left in one source layer, or zero.</summary>
        public float AmountOn(ModifierSource source)
        {
            for (var i = 0; i < layers.Count; i++)
            {
                if (layers[i].Source == source)
                {
                    return layers[i].Amount;
                }
            }

            return 0f;
        }

        /// <summary>Seconds left on one source layer, or zero.</summary>
        public float RemainingOn(ModifierSource source)
        {
            for (var i = 0; i < layers.Count; i++)
            {
                if (layers[i].Source == source)
                {
                    return layers[i].Remaining;
                }
            }

            return 0f;
        }

        /// <summary>
        /// Takes as much of the damage as the shields can, soonest-to-expire first.
        /// </summary>
        /// <returns>How much was absorbed. The caller subtracts the rest from health.</returns>
        public float Absorb(float damage)
        {
            if (damage <= 0f || layers.Count == 0)
            {
                return 0f;
            }

            var absorbed = 0f;
            var left = damage;

            // Repeated minimum search rather than a sort: this runs on every hit at horde density and
            // the list is a handful of entries, where scanning beats allocating a sorted copy.
            while (left > 0f && layers.Count > 0)
            {
                var index = SoonestIndex();
                var layer = layers[index];

                if (layer.Amount > left)
                {
                    layer.Amount -= left;
                    layers[index] = layer;
                    absorbed += left;
                    left = 0f;
                    break;
                }

                absorbed += layer.Amount;
                left -= layer.Amount;

                layers.RemoveAt(index);

                // A layer that absorbed the last of itself is spent, not expired — there is nothing
                // left over to announce, and reporting it as an expiry would fire "your shield ran
                // out" on the shield that did its job.
            }

            total -= absorbed;

            if (total < 0f)
            {
                total = 0f;
            }

            Changed?.Invoke();

            return absorbed;
        }

        /// <summary>Advances every layer and drops the ones that ran out.</summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f || layers.Count == 0)
            {
                return;
            }

            List<Layer> ended = null;

            for (var i = layers.Count - 1; i >= 0; i--)
            {
                var layer = layers[i];

                layer.Remaining -= deltaTime;

                if (layer.Remaining > 0f)
                {
                    layers[i] = layer;
                    continue;
                }

                layers.RemoveAt(i);
                total -= layer.Amount;

                (ended ??= new List<Layer>()).Add(layer);
            }

            if (ended == null)
            {
                return;
            }

            if (total < 0f)
            {
                total = 0f;
            }

            // Announced after the list has settled, so a handler that adds a shield of its own is not
            // adding it to a collection still being walked.
            for (var i = 0; i < ended.Count; i++)
            {
                Announce(ended[i].Source, ended[i].Amount);
            }

            Changed?.Invoke();
        }

        void Announce(ModifierSource source, float unused)
        {
            if (unused > 0f)
            {
                Expired?.Invoke(source, unused);
            }
        }

        int SoonestIndex()
        {
            var index = 0;

            for (var i = 1; i < layers.Count; i++)
            {
                if (layers[i].Remaining < layers[index].Remaining)
                {
                    index = i;
                }
            }

            return index;
        }
    }
}
