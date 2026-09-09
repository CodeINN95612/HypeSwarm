using System;
using System.Collections.Generic;
using HypeSwarm.Shared.Content;

namespace HypeSwarm.Shared.Stats
{
    /// <summary>
    /// One entity worth of stats: base values, the modifiers acting on them, and the resolved
    /// numbers everything else reads.
    /// </summary>
    /// <remarks>
    /// <b>No final number is ever stored</b> (§9). The sheet holds bases and a list of modifiers and
    /// recomputes a stat the first time anyone asks after it changed. The alternative — writing the
    /// resolved value back on every change — is how a stat sheet ends up half a percent wrong after
    /// forty buffs with nobody able to say which one did it.
    ///
    /// <para>Resolution is the fixed order and nothing else:
    /// <c>(base + flatAdd) × (1 + Σ percentAdd) × Π (1 + percentMult)</c>. Because the operations are
    /// grouped before they are applied, <b>the order modifiers arrive in cannot change the
    /// outcome</b>. That is the property that makes augment pick order irrelevant, and that makes a
    /// replicated modifier list safe to rebuild in any sequence.</para>
    ///
    /// <para>Pure C#: no Unity types, no clock, no network. A trash mob, a champion, a boss and a
    /// test all use the same class, and none of them pay for anything the others need.</para>
    /// </remarks>
    public sealed class StatSheet
    {
        readonly StatCatalog catalog;
        readonly float[] baseValues = new float[StatIds.Count];
        readonly float[] resolved = new float[StatIds.Count];
        readonly bool[] stale = new bool[StatIds.Count];
        readonly List<StatModifier> modifiers = new List<StatModifier>();

        public StatSheet(StatCatalog catalog = null)
        {
            this.catalog = catalog ?? StatCatalog.Default;

            for (var i = 0; i < baseValues.Length; i++)
            {
                baseValues[i] = this.catalog.Definitions[i].BaseValue;
                stale[i] = true;
            }
        }

        public StatCatalog Catalog => catalog;

        /// <summary>
        /// A stat changed and anything displaying it should read it again. Raised once per affected
        /// stat per change, before the new value is computed — computing on demand is the whole
        /// point of the dirty flag.
        /// </summary>
        public event Action<StatId> Changed;

        /// <summary>Every modifier on the sheet, in the order it was added.</summary>
        public IReadOnlyList<StatModifier> Modifiers => modifiers;

        /// <summary>What the stat starts at, before any modifier.</summary>
        public float GetBase(StatId stat) => baseValues[(int)stat];

        /// <summary>
        /// Sets the starting value. Where champion growth writes on level-up (§5.6.3) — growth is a
        /// bigger base, not a pile of modifiers nobody can ever remove.
        /// </summary>
        public void SetBase(StatId stat, float value)
        {
            var index = (int)stat;

            if (baseValues[index].Equals(value))
            {
                return;
            }

            baseValues[index] = value;
            Invalidate(stat);
        }

        /// <summary>The resolved value. Cached until something changes it.</summary>
        public float Get(StatId stat)
        {
            var index = (int)stat;

            if (stale[index])
            {
                resolved[index] = Resolve(stat);
                stale[index] = false;
            }

            return resolved[index];
        }

        /// <summary>
        /// What the stat actually does. For a linear stat that is the value itself; for a derived
        /// one it is the bounded fraction from <see cref="StatCurve"/> — the share of the way to the
        /// asymptote, in <c>(-1, 1)</c>.
        /// </summary>
        /// <remarks>
        /// What the fraction means is for the caller to decide: damage removed for damage reduction,
        /// cooldown removed for haste, bonus speed for move speed. The sheet does not know and must
        /// not — that would be behaviour, and stats have none (§5.6.5).
        /// </remarks>
        public float Effect(StatId stat)
        {
            var definition = catalog[stat];

            return definition.IsDerived
                ? StatCurve.Diminishing(Get(stat), definition.SoftCap)
                : Get(stat);
        }

        public void Add(StatModifier modifier)
        {
            modifiers.Add(modifier);
            Invalidate(modifier.Stat);
        }

        public void Add(IEnumerable<StatModifier> incoming)
        {
            if (incoming == null)
            {
                return;
            }

            foreach (var modifier in incoming)
            {
                Add(modifier);
            }
        }

        /// <summary>
        /// Removes everything one source applied. The only removal the game needs: unequipping, buff
        /// expiry and augment loss are all this call (§9).
        /// </summary>
        /// <returns>How many modifiers went.</returns>
        public int RemoveSource(ModifierSource source)
        {
            return RemoveWhere(modifier => modifier.Source == source);
        }

        /// <summary>
        /// Removes every instance of a piece of content — both copies of an item at once. Rarer than
        /// <see cref="RemoveSource"/> and easy to reach for by mistake, so it is named differently.
        /// </summary>
        public int RemoveContent(ContentId content)
        {
            return RemoveWhere(modifier => modifier.Source.Content.Equals(content));
        }

        /// <summary>Drops every modifier. Bases are untouched.</summary>
        public void Clear()
        {
            RemoveWhere(_ => true);
        }

        /// <summary>How many modifiers this source currently has on the sheet.</summary>
        public int CountFrom(ModifierSource source)
        {
            var count = 0;

            for (var i = 0; i < modifiers.Count; i++)
            {
                if (modifiers[i].Source == source)
                {
                    count++;
                }
            }

            return count;
        }

        int RemoveWhere(Func<StatModifier, bool> match)
        {
            var removed = 0;
            var touched = new bool[StatIds.Count];

            for (var i = modifiers.Count - 1; i >= 0; i--)
            {
                if (!match(modifiers[i]))
                {
                    continue;
                }

                touched[(int)modifiers[i].Stat] = true;
                modifiers.RemoveAt(i);
                removed++;
            }

            if (removed == 0)
            {
                return 0;
            }

            for (var i = 0; i < touched.Length; i++)
            {
                if (touched[i])
                {
                    Invalidate((StatId)i);
                }
            }

            return removed;
        }

        void Invalidate(StatId stat)
        {
            stale[(int)stat] = true;
            Changed?.Invoke(stat);
        }

        float Resolve(StatId stat)
        {
            var definition = catalog[stat];

            var flat = 0f;
            var percentAdd = 0f;
            var percentMult = 1f;

            // One pass, three accumulators. Grouping here rather than sorting the list is what makes
            // insertion order irrelevant without paying for a sort on every change.
            for (var i = 0; i < modifiers.Count; i++)
            {
                var modifier = modifiers[i];

                if (modifier.Stat != stat)
                {
                    continue;
                }

                switch (modifier.Operation)
                {
                    case ModifierOperation.FlatAdd:
                        flat += modifier.Value;
                        break;

                    case ModifierOperation.PercentAdd:
                        percentAdd += modifier.Value;
                        break;

                    case ModifierOperation.PercentMult:
                        percentMult *= 1f + modifier.Value;
                        break;
                }
            }

            var value = (baseValues[(int)stat] + flat) * (1f + percentAdd) * percentMult;

            return value < definition.Minimum ? definition.Minimum : value;
        }
    }
}
