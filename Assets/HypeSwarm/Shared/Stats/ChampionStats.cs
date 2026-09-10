using System;
using System.Collections.Generic;
using HypeSwarm.Shared.Tuning;
using Mirror;
using UnityEngine;

namespace HypeSwarm.Shared.Stats
{
    /// <summary>
    /// The stat sheet of one networked entity, kept the same on every machine by replicating the
    /// modifiers rather than the numbers.
    /// </summary>
    /// <remarks>
    /// <b>Modifier lists, never values</b> (Phase 2 step 4). Sending resolved numbers would be
    /// smaller and would be wrong: a client that only receives "damage is 47" cannot say why, cannot
    /// show what the last augment did, and drifts silently the moment one machine resolves in a
    /// different order. Sending the modifiers means every machine computes the same sheet from the
    /// same inputs, and the tooltip that explains where a number came from is free.
    ///
    /// <para>Server-authoritative, unlike movement (§10). Movement is client-authoritative because
    /// latency on your own champion is the thing players feel; stats are the opposite — nobody feels
    /// a buff arriving 60ms late, and a client that could write its own damage stat is a client that
    /// can write any number it likes.</para>
    ///
    /// <para>Base values are not replicated. Every machine derives them from the same catalog, so
    /// they are already identical; when champion growth arrives (§5.6.3) it is the level and the
    /// champion that replicate, and the bases stay derived. A number that both machines can compute
    /// is a number that should never travel.</para>
    /// </remarks>
    [AddComponentMenu("Hype Swarm/Champion Stats")]
    [DisallowMultipleComponent]
    public sealed class ChampionStats : NetworkBehaviour
    {
        readonly SyncList<StatModifier> replicated = new SyncList<StatModifier>();

        /// <summary>Expiry timers. Server-side only — the clients learn about it through the list.</summary>
        readonly StatBuffs buffs = new StatBuffs();

        StatSheet sheet;

        /// <summary>
        /// The resolved stats on this machine. Read it; the server is what writes.
        /// </summary>
        /// <remarks>
        /// Built on first read rather than in <c>Awake</c>. Unity does not order <c>Awake</c> between
        /// components on one GameObject, and <see cref="Combat.Health"/> wants this sheet in its own
        /// — a bug that depends on which ran first reproduces on one machine in five.
        ///
        /// <para>The tuned catalog, not the compiled defaults: soft caps and starting values are
        /// balance, and balance lives in the config file (§13.3).</para>
        /// </remarks>
        public StatSheet Sheet => sheet ??= new StatSheet(GameTuning.Stats);

        /// <summary>
        /// The sheet was rebuilt from a change that arrived over the network. Cheaper to subscribe
        /// to than <see cref="StatSheet.Changed"/> when a listener redraws everything anyway.
        /// </summary>
        public event Action Replicated;

        void Awake()
        {
            // Touch it so the sheet exists before anything replicates into it.
            _ = Sheet;

            replicated.Callback += OnListChanged;

            buffs.Applied += OnBuffApplied;
            buffs.Expired += OnBuffExpired;
        }

        public override void OnStartClient()
        {
            // The initial list arrives in the spawn payload without firing a single callback, so a
            // player joining a run in progress would otherwise see a champion with no items on it.
            Rebuild();
        }

        void Update()
        {
            if (!isServer)
            {
                return;
            }

            buffs.Tick(Time.deltaTime);
        }

        // --- Server API --------------------------------------------------------------------

        /// <summary>
        /// Applies modifiers that stay until something removes them. Items, augments, level-up
        /// growth that is not a base change.
        /// </summary>
        [Server]
        public void AddPermanent(ModifierSource source, params StatModifier[] modifiers)
        {
            if (modifiers == null)
            {
                return;
            }

            for (var i = 0; i < modifiers.Length; i++)
            {
                replicated.Add(modifiers[i].From(source));
            }
        }

        /// <summary>Applies modifiers for a number of seconds, then takes them off again.</summary>
        [Server]
        public void ApplyBuff(ModifierSource source, float duration, params StatModifier[] modifiers)
        {
            buffs.Apply(source, duration, modifiers);
        }

        /// <summary>
        /// Removes everything from one source, whether it was permanent or timed. Unequipping,
        /// dispelling, selling.
        /// </summary>
        [Server]
        public void RemoveSource(ModifierSource source)
        {
            // Cancel the timer first: its own expiry would otherwise arrive later and remove
            // whatever the same source had applied in the meantime.
            buffs.Remove(source);
            RemoveFromList(source);
        }

        /// <summary>Seconds left on a timed buff. Zero on a client, which does not run the timers.</summary>
        public float RemainingOn(ModifierSource source) => buffs.RemainingOn(source);

        // --- Replication -------------------------------------------------------------------

        void OnBuffApplied(ModifierSource source, IReadOnlyList<StatModifier> modifiers)
        {
            for (var i = 0; i < modifiers.Count; i++)
            {
                replicated.Add(modifiers[i]);
            }
        }

        void OnBuffExpired(ModifierSource source) => RemoveFromList(source);

        void RemoveFromList(ModifierSource source)
        {
            for (var i = replicated.Count - 1; i >= 0; i--)
            {
                if (replicated[i].Source == source)
                {
                    replicated.RemoveAt(i);
                }
            }
        }

        void OnListChanged(SyncList<StatModifier>.Operation operation, int index, StatModifier oldItem, StatModifier newItem)
        {
            Rebuild();
        }

        /// <summary>
        /// Rebuilds the sheet from the replicated list.
        /// </summary>
        /// <remarks>
        /// The whole list, every time, rather than applying the one operation that changed. It is
        /// O(n) on a list of tens, it is the same code path on the host and on a client, and it
        /// cannot drift — an incremental version has to get add, remove, clear and the initial
        /// payload all exactly right, and the failure mode is a stat that is wrong on one machine
        /// only. If a profile ever says this matters, the fix is to rebuild once per frame instead
        /// of once per operation, not to hand-apply the deltas.
        /// </remarks>
        void Rebuild()
        {
            sheet.Clear();

            for (var i = 0; i < replicated.Count; i++)
            {
                var modifier = replicated[i];

                if (!StatIds.IsDefined(modifier.Stat))
                {
                    // A host on a newer build naming a stat this one does not have. Dropping it
                    // keeps the sheet sane; the mismatch itself is a problem for the version check.
                    Debug.LogWarning($"[Stats] Ignoring a modifier for unknown stat {(int)modifier.Stat}.", this);
                    continue;
                }

                sheet.Add(modifier);
            }

            Replicated?.Invoke();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Source of the buff the network panel applies. Development only.</summary>
        public const string DebugBuffId = "debug.stat_buff";

        /// <summary>
        /// Asks the host for a temporary buff on this champion, so the exit criterion for this step
        /// — a buff applies, shows up on every client, and comes off cleanly — can be watched rather
        /// than argued about.
        /// </summary>
        /// <remarks>
        /// Compiled out of a release build. It is a command that lets a client hand itself stats,
        /// which is exactly the thing the server-authoritative design above exists to prevent, and
        /// the development build flag is what keeps that honest.
        /// </remarks>
        [Command]
        public void CmdDebugBuff(float duration)
        {
            var source = ModifierSource.Parse(DebugBuffId);

            ApplyBuff(
                source,
                duration,
                StatModifier.Flat(StatId.Damage, 25f, source),
                StatModifier.PercentAdd(StatId.MaxHealth, 0.5f, source),
                StatModifier.Flat(StatId.Haste, 100f, source));
        }
#endif
    }
}
