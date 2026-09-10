using System;
using HypeSwarm.Shared.Abilities;
using HypeSwarm.Shared.Stats;
using HypeSwarm.Shared.Tuning;
using Mirror;
using UnityEngine;

namespace HypeSwarm.Shared.Combat
{
    /// <summary>
    /// The health of one networked entity: a server-owned <see cref="HealthPool"/> with the resulting
    /// numbers replicated out to everyone.
    /// </summary>
    /// <remarks>
    /// <b>Values are replicated here, and that is not the inconsistency it looks like.</b>
    /// <see cref="ChampionStats"/> replicates modifier lists rather than resolved numbers because
    /// every machine holds the same inputs and can do the same arithmetic. Health has no such inputs:
    /// it is the running total of damage events that were never replicated, resolved against dodge
    /// rolls that only the host made. There is nothing for a client to recompute, so the number itself
    /// is the smallest correct thing to send.
    ///
    /// <para>Server-authoritative without exception (§10). A client asking for damage is a client
    /// writing its own health, so nothing here is callable from one outside a development build.</para>
    ///
    /// <para>The stat sheet comes from <see cref="ChampionStats"/> when there is one, and is created
    /// locally when there is not. That is what lets the same component sit on an elite or a
    /// destructible structure, neither of which replicates a modifier list.</para>
    /// </remarks>
    [AddComponentMenu("Hype Swarm/Health")]
    [DisallowMultipleComponent]
    public sealed class Health : NetworkBehaviour, ICombatant
    {
        [SerializeField]
        [SyncVar]
        [Tooltip("Which side this is on. Players for a champion and anything it summons, Enemies for " +
                 "the horde, Neutral for something that is only hit on purpose.")]
        Faction faction = Faction.Players;

        [SyncVar(hook = nameof(OnNumberChanged))]
        float networkCurrent = HealthPool.MinimumMax;

        [SyncVar(hook = nameof(OnNumberChanged))]
        float networkMax = HealthPool.MinimumMax;

        [SyncVar(hook = nameof(OnNumberChanged))]
        float networkShield;

        [SyncVar(hook = nameof(OnDeadChanged))]
        bool networkDead;

        ChampionStats championStats;
        HealthPool pool;
        StatSheet sheet;
        DamageRandom random;

        /// <summary>The pool, on the server. Null on a client, which owns no health state.</summary>
        public HealthPool Pool => pool;

        public float Current => networkCurrent;

        public float Max => networkMax;

        public float Shield => networkShield;

        public float Fraction => networkMax <= 0f ? 0f : networkCurrent / networkMax;

        public bool IsDead => networkDead;

        /// <summary>Which side this is on. Authored on the prefab, replicated so clients can aim too.</summary>
        public Faction Faction => faction;

        /// <summary>Network id, for the damage packets this entity sends.</summary>
        public uint Id => netId;

        /// <summary>Where this is. What targeting measures distance against.</summary>
        public Vector3 Position => transform.position;

        /// <summary>
        /// The stats this entity scales from, on every machine.
        /// </summary>
        /// <remarks>
        /// Resolved in <c>Awake</c> rather than on spawn, because a client has to resolve targets for
        /// its own predicted casts and a null sheet would make every coefficient read as its flat part.
        /// </remarks>
        public StatSheet Stats => sheet;

        /// <summary>The numbers moved. Raised on every machine, which is what a health bar needs.</summary>
        public event Action Changed;

        /// <summary>This entity died. Raised on every machine.</summary>
        public event Action Died;

        /// <summary>Damage landed. <b>Server only</b> — clients never see individual hits.</summary>
        public event Action<DamageResult> Damaged;

        void Awake()
        {
            TryGetComponent(out championStats);

            // ChampionStats builds its sheet lazily, so this does not depend on which Awake Unity ran
            // first — component order inside a GameObject is undefined, and a bug that depends on it
            // reproduces on one machine in five.
            sheet = championStats != null ? championStats.Sheet : new StatSheet(GameTuning.Stats);
        }

        /// <summary>
        /// Joins the set of things abilities can find.
        /// </summary>
        /// <remarks>
        /// Enable and disable rather than spawn and despawn, so this works the same for a scene-placed
        /// dummy, a spawned champion, and a pooled enemy later on. Registration is local to each
        /// machine: the host needs it to resolve damage and the owner needs it to predict (§11).
        /// </remarks>
        void OnEnable() => CombatantWorld.Shared.Register(this);

        void OnDisable() => CombatantWorld.Shared.Unregister(this);

        public override void OnStartServer()
        {
            pool = new HealthPool(sheet.Get(StatId.MaxHealth));
            pool.Changed += Publish;
            pool.Damaged += OnPoolDamaged;
            pool.Died += OnPoolDied;
            pool.Revived += OnPoolRevived;

            sheet.Changed += OnStatChanged;

            random = new DamageRandom(DamageRandom.SeedFrom(netId));

            Publish();
        }

        public override void OnStopServer()
        {
            if (sheet != null)
            {
                sheet.Changed -= OnStatChanged;
            }

            if (pool == null)
            {
                return;
            }

            pool.Changed -= Publish;
            pool.Damaged -= OnPoolDamaged;
            pool.Died -= OnPoolDied;
            pool.Revived -= OnPoolRevived;
        }

        void Update()
        {
            if (!isServer || pool == null)
            {
                return;
            }

            var settings = GameTuning.Combat;

            pool.Tick(Time.deltaTime, sheet.Get(StatId.HealthRegen), settings.RegenDelay);
        }

        // --- Server API --------------------------------------------------------------------

        /// <summary>
        /// Takes a packet. <b>Call <see cref="DamagePipeline.Apply"/> rather than this</b> — the
        /// pipeline is the choke point every hook will live in, and reaching past it is how damage
        /// ends up not being modified by half the augments in the game.
        /// </summary>
        [Server]
        public DamageResult TakeDamage(in DamagePacket packet)
        {
            if (pool == null || pool.IsDead)
            {
                return DamageResult.None;
            }

            var attacker = Resolve(packet.AttackerNetId);
            var attackerSheet = attacker != null && attacker.TryGetComponent(out ChampionStats stats)
                ? stats.Sheet
                : null;

            var mitigation = DamagePipeline.Mitigate(packet, attackerSheet, sheet, random.NextFloat());
            var result = pool.Apply(mitigation);

            if (attackerSheet != null && result.Dealt > 0f)
            {
                Drain(attacker, attackerSheet, result.Dealt);
            }

            return result;
        }

        [Server]
        public float Heal(float amount) => pool == null ? 0f : pool.Heal(amount);

        /// <summary>Adds absorption in front of health. Removable by source like any other effect.</summary>
        [Server]
        public bool AddShield(ModifierSource source, float amount, float duration)
        {
            return pool != null && pool.Shields.Add(source, amount, duration);
        }

        [Server]
        public bool RemoveShield(ModifierSource source)
        {
            return pool != null && pool.Shields.Remove(source);
        }

        /// <summary>
        /// Applies timed stat modifiers — a slow, a shred, a defensive stance.
        /// </summary>
        /// <remarks>
        /// Forwarded to <see cref="ChampionStats"/>, which is what replicates them, and refused when
        /// there is none. Returning false rather than applying them to the local sheet is the honest
        /// answer: a modifier only this machine knows about is a number that differs per machine, which
        /// is the one thing the stat design exists to prevent.
        /// </remarks>
        [Server]
        public bool ApplyModifiers(ModifierSource source, float duration, StatModifier[] modifiers)
        {
            if (championStats == null || modifiers == null || modifiers.Length == 0)
            {
                return false;
            }

            championStats.ApplyBuff(source, duration, modifiers);

            return true;
        }

        /// <summary>Brings this entity back at the tuned share of maximum health.</summary>
        [Server]
        public void Revive() => pool?.Revive(GameTuning.Combat.ReviveHealthFraction);

        /// <summary>Kills outright, ignoring shields and mitigation.</summary>
        [Server]
        public void Kill() => pool?.Kill();

        // --- Internals ---------------------------------------------------------------------

        /// <summary>
        /// Lifesteal: the attacker heals for a share of what landed.
        /// </summary>
        /// <remarks>
        /// Read off <see cref="DamageResult.Dealt"/>, which excludes overkill — otherwise a hit for a
        /// thousand on a trash mob with two health would heal for a thousand, and the best lifesteal
        /// build in the game would be the one that overkills hardest.
        /// </remarks>
        static void Drain(NetworkIdentity attacker, StatSheet attackerSheet, float dealt)
        {
            var share = attackerSheet.Effect(StatId.Lifesteal);

            if (share <= 0f)
            {
                return;
            }

            if (attacker.TryGetComponent(out Health health))
            {
                health.Heal(dealt * share);
            }
        }

        static NetworkIdentity Resolve(uint attackerNetId)
        {
            if (attackerNetId == 0u)
            {
                return null;
            }

            return NetworkServer.spawned.TryGetValue(attackerNetId, out var identity) ? identity : null;
        }

        void OnStatChanged(StatId stat)
        {
            if (stat == StatId.MaxHealth)
            {
                pool.SetMax(sheet.Get(StatId.MaxHealth));
            }
        }

        /// <summary>Pushes the pool out to every machine. The only writer of the sync vars.</summary>
        void Publish()
        {
            networkCurrent = pool.Current;
            networkMax = pool.Max;
            networkShield = pool.Shield;
            networkDead = pool.IsDead;

            // Raised here rather than left to the hooks, which Mirror does not call consistently on a
            // host. One announcement per change on the server, one per change on a client.
            Changed?.Invoke();
        }

        void OnPoolDamaged(DamageResult result) => Damaged?.Invoke(result);

        void OnPoolDied(DamageResult result)
        {
            Publish();
            Died?.Invoke();
        }

        void OnPoolRevived() => Publish();

        void OnNumberChanged(float previous, float current)
        {
            if (!isServer)
            {
                Changed?.Invoke();
            }
        }

        void OnDeadChanged(bool previous, bool dead)
        {
            if (!isServer && dead)
            {
                Died?.Invoke();
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Source of the damage and the shield the network panel applies. Development only.</summary>
        public const string DebugHitId = "debug.hit";

        /// <summary>
        /// Damages this champion, so the exit criterion for this step can be watched rather than
        /// argued about: health drops on every machine, a shield eats it first, regeneration comes
        /// back after the delay, and zero is a death rather than a negative number.
        /// </summary>
        /// <remarks>
        /// Compiled out of a release build. It is a command that lets a client damage a champion,
        /// which is exactly what the server-authoritative design exists to prevent.
        /// </remarks>
        [Command]
        public void CmdDebugDamage(float amount)
        {
            DamagePipeline.Apply(this, new DamagePacket(amount, ModifierSource.Parse(DebugHitId), DamageFlags.Ability, netId));
        }

        [Command]
        public void CmdDebugShield(float amount, float duration)
        {
            AddShield(ModifierSource.Parse(DebugHitId), amount, duration);
        }

        [Command]
        public void CmdDebugRevive() => Revive();

        /// <summary>
        /// Brings every hostile back, so a training dummy can be killed more than once in a session.
        /// </summary>
        /// <remarks>
        /// Reaches across entities, which nothing in the real game does or should: a command is
        /// authorised by owning the object it is called on, and this one is called on your own champion to
        /// affect somebody else's. Compiled out of a release build for exactly that reason.
        /// </remarks>
        [Command]
        public void CmdDebugReviveHostiles()
        {
            var all = CombatantWorld.Shared.All;

            for (var i = 0; i < all.Count; i++)
            {
                if (all[i] is Health health && health.faction != Faction.Players)
                {
                    health.Revive();
                }
            }
        }
#endif
    }
}
