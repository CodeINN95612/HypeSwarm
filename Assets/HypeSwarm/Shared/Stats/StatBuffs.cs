using System;
using System.Collections.Generic;

namespace HypeSwarm.Shared.Stats
{
    /// <summary>
    /// Modifiers that expire. Holds the schedule and announces the two moments that matter; it does
    /// not touch a stat sheet itself.
    /// </summary>
    /// <remarks>
    /// The separation is what makes this usable on the network. On a host the buff has to reach four
    /// other machines, so what listens to <see cref="Applied"/> is a replicated modifier list rather
    /// than a sheet; on a trash mob nothing is replicated and the sheet is the only listener. One
    /// timer either way, and no version of it that writes to two places and drifts.
    /// <see cref="Bind"/> is the one-line wiring for the simple case.
    ///
    /// <para>Time is passed in, never read. No wall clock and no <c>Time.deltaTime</c>, for the same
    /// reason the movement code does the same thing: a schedule that reads a clock cannot be tested
    /// and cannot be re-simulated.</para>
    ///
    /// <para>Permanent modifiers do not belong here. An item and an augment go straight onto the
    /// sheet and come off by source when they are lost; wrapping them in a timer that never fires
    /// would only add a place for them to be forgotten.</para>
    /// </remarks>
    public sealed class StatBuffs
    {
        readonly List<Entry> entries = new List<Entry>();

        /// <summary>A buff started and these modifiers should now apply.</summary>
        public event Action<ModifierSource, IReadOnlyList<StatModifier>> Applied;

        /// <summary>
        /// A buff ended, by expiry, by cancellation, or because it was refreshed. Everything from
        /// this source should come off.
        /// </summary>
        public event Action<ModifierSource> Expired;

        /// <summary>How many buffs are running.</summary>
        public int Count => entries.Count;

        /// <summary>The running buffs, soonest to expire last — insertion order, not sorted.</summary>
        public IEnumerable<ModifierSource> Sources
        {
            get
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    yield return entries[i].Source;
                }
            }
        }

        /// <summary>
        /// Applies modifiers for a while.
        /// </summary>
        /// <remarks>
        /// <b>A second call from the same source refreshes rather than stacks</b>: the old modifiers
        /// are announced as expired and the new ones applied, so a re-applied buff cannot leave a
        /// copy of itself behind. Stacking is a source per stack — that is what
        /// <see cref="ModifierSource.Instance"/> is for — and making it explicit is what stops a
        /// re-application loop from doubling a buff forever.
        /// </remarks>
        /// <returns>False if there was nothing to apply, or the duration was not positive.</returns>
        public bool Apply(ModifierSource source, float duration, IReadOnlyList<StatModifier> modifiers)
        {
            if (modifiers == null || modifiers.Count == 0 || duration <= 0f || float.IsNaN(duration))
            {
                return false;
            }

            Remove(source);

            var copy = new StatModifier[modifiers.Count];

            for (var i = 0; i < modifiers.Count; i++)
            {
                // Stamped with the source rather than trusted to carry it. A caller building a buff
                // from an authored template would otherwise apply modifiers that no removal matches.
                copy[i] = modifiers[i].From(source);
            }

            entries.Add(new Entry(source, duration, copy));
            Applied?.Invoke(source, copy);

            return true;
        }

        /// <summary>Applies one modifier for a while.</summary>
        public bool Apply(ModifierSource source, float duration, StatModifier modifier)
        {
            return Apply(source, duration, new[] { modifier });
        }

        /// <summary>Ends a buff early. Dispelling, and how a refresh clears the previous one.</summary>
        public bool Remove(ModifierSource source)
        {
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].Source != source)
                {
                    continue;
                }

                entries.RemoveAt(i);
                Expired?.Invoke(source);

                return true;
            }

            return false;
        }

        /// <summary>Ends every buff. For death, and for a run ending.</summary>
        public void Clear()
        {
            // Copied first: a listener that reacts by applying something else would otherwise be
            // modifying the list this loop is walking.
            var ending = entries.ToArray();

            entries.Clear();

            for (var i = 0; i < ending.Length; i++)
            {
                Expired?.Invoke(ending[i].Source);
            }
        }

        /// <summary>
        /// What a running buff applied, or an empty list. For the readout, and for anything that
        /// needs to reapply a buff without knowing where it came from.
        /// </summary>
        public IReadOnlyList<StatModifier> ModifiersOn(ModifierSource source)
        {
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].Source == source)
                {
                    return entries[i].Modifiers;
                }
            }

            return Array.Empty<StatModifier>();
        }

        /// <summary>Seconds left on a buff, or zero if that source has none running.</summary>
        public float RemainingOn(ModifierSource source)
        {
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].Source == source)
                {
                    return entries[i].Remaining;
                }
            }

            return 0f;
        }

        /// <summary>Advances every timer and raises <see cref="Expired"/> for anything that ran out.</summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f || entries.Count == 0)
            {
                return;
            }

            List<ModifierSource> finished = null;

            for (var i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                entry.Remaining -= deltaTime;

                if (entry.Remaining > 0f)
                {
                    entries[i] = entry;
                    continue;
                }

                entries.RemoveAt(i);
                finished ??= new List<ModifierSource>();
                finished.Add(entry.Source);
            }

            if (finished == null)
            {
                return;
            }

            // Raised after the list is settled, so a listener that applies a new buff in response
            // does not land in a collection still being iterated.
            for (var i = 0; i < finished.Count; i++)
            {
                Expired?.Invoke(finished[i]);
            }
        }

        /// <summary>
        /// Wires this schedule straight to a sheet, for everything that is not replicated. Returns
        /// itself so it can be constructed and bound in one expression.
        /// </summary>
        public StatBuffs Bind(StatSheet sheet)
        {
            if (sheet == null)
            {
                throw new ArgumentNullException(nameof(sheet));
            }

            Applied += (_, modifiers) => sheet.Add(modifiers);
            Expired += source => sheet.RemoveSource(source);

            return this;
        }

        struct Entry
        {
            public Entry(ModifierSource source, float duration, StatModifier[] modifiers)
            {
                Source = source;
                Remaining = duration;
                Modifiers = modifiers;
            }

            public ModifierSource Source { get; }

            public float Remaining { get; set; }

            public StatModifier[] Modifiers { get; }
        }
    }
}
