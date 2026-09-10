using System;
using HypeSwarm.Shared.Combat;
using HypeSwarm.Shared.Stats;
using UnityEngine;

namespace HypeSwarm.ClientOnly.Presentation
{
    /// <summary>
    /// Shows what is happening <i>to</i> a combatant: a flash when it is hit, a tint while it is slowed or
    /// dead, and a ring while it is shielded.
    /// </summary>
    /// <remarks>
    /// Reads only what is already replicated — health, shield, the dead flag and the stat sheet — so it
    /// shows the same thing on every machine without a message of its own. A hit is a drop in health, not
    /// a damage event: individual hits are never sent to clients (health replicates as values), and a
    /// flash driven by anything else would flash on the host only.
    ///
    /// <para><b>The flash is proportional, with a floor.</b> A passive aura or a burn ticks several times
    /// a second for a sliver of health each time; flashing at full strength on every tick turned the target
    /// permanently white in the first build of this, and a flash that never stops says nothing. So small
    /// ticks do not flash at all and real hits flash in proportion to what they took.</para>
    ///
    /// <para><b>Prototype scaffolding for champions and dummies, not for the horde.</b> It polls a stat and
    /// sets a property block every frame, which is nothing at a dozen entities and a real cost at two
    /// thousand. Trash mobs in Phase 2 step 7 are instanced and will carry none of this.</para>
    /// </remarks>
    [AddComponentMenu("Hype Swarm/Combatant Feedback Presenter")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Health))]
    public sealed class CombatantFeedbackPresenter : MonoBehaviour
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [SerializeField]
        [Tooltip("The renderers to tint. Their material colour is the rest colour.")]
        Renderer[] renderers = Array.Empty<Renderer>();

        [SerializeField]
        [Tooltip("Unlit material for the shield ring. Leave empty for no ring.")]
        Material lineMaterial;

        [Header("Colours")]
        [SerializeField]
        Color hitColor = Color.white;

        [SerializeField]
        Color slowedColor = new Color(0.35f, 0.6f, 1f);

        [SerializeField]
        Color deadColor = new Color(0.12f, 0.12f, 0.14f);

        [SerializeField]
        Color shieldColor = new Color(0.45f, 0.85f, 1f);

        [Header("Hit flash")]
        [SerializeField]
        [Tooltip("Seconds a hit flash lasts.")]
        float flashSeconds = 0.15f;

        [SerializeField]
        [Tooltip("A hit smaller than this share of maximum health does not flash at all, so an aura or a " +
                 "burn ticking several times a second does not turn its target permanently white.")]
        [Range(0f, 0.2f)]
        float flashThreshold = 0.03f;

        [SerializeField]
        [Tooltip("The share of maximum health at which a hit flashes at full strength. Smaller hits flash " +
                 "fainter in proportion.")]
        [Range(0.01f, 1f)]
        float fullFlashShare = 0.2f;

        [Header("Shield")]
        [SerializeField]
        float shieldRadius = 1.1f;

        [SerializeField]
        float shieldWidth = 0.09f;

        Health health;
        ChampionStats stats;
        MaterialPropertyBlock block;
        Color[] restColours;
        GroundOutline shieldRing;
        float lastHealth;
        float flashRemaining;
        float flashStrength;

        void Awake()
        {
            health = GetComponent<Health>();
            TryGetComponent(out stats);

            block = new MaterialPropertyBlock();
            restColours = new Color[renderers.Length];

            for (var i = 0; i < renderers.Length; i++)
            {
                var material = renderers[i] != null ? renderers[i].sharedMaterial : null;

                restColours[i] = material != null && material.HasProperty(BaseColor)
                    ? material.GetColor(BaseColor)
                    : Color.white;
            }
        }

        void OnEnable()
        {
            lastHealth = health.Current;
            health.Changed += OnHealthChanged;
        }

        void OnDisable()
        {
            health.Changed -= OnHealthChanged;
            flashRemaining = 0f;
            flashStrength = 0f;
            shieldRing?.Hide();

            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].SetPropertyBlock(null);
                }
            }
        }

        void OnHealthChanged()
        {
            var current = health.Current;
            var drop = lastHealth - current;

            lastHealth = current;

            // A rise is regeneration or a heal and gets no flash: regeneration ticks constantly too.
            if (drop <= 0f || health.Max <= 0f)
            {
                return;
            }

            var share = drop / health.Max;

            if (share < flashThreshold)
            {
                return;
            }

            var strength = Mathf.Clamp01(share / fullFlashShare);

            // A faint hit landing during a strong flash must not cut the strong one short.
            var fading = flashSeconds > 0f ? flashStrength * (flashRemaining / flashSeconds) : 0f;

            flashStrength = Mathf.Max(strength, fading);
            flashRemaining = flashSeconds;
        }

        void LateUpdate()
        {
            if (flashRemaining > 0f)
            {
                flashRemaining = Mathf.Max(0f, flashRemaining - Time.deltaTime);
            }

            var dead = health.IsDead;

            // A slow is negative move speed and nothing more (§5.6.5), so it is read straight off the
            // replicated sheet — the tint shows on every machine because the modifier does.
            var slowed = !dead && stats != null && stats.Sheet != null && stats.Sheet.Effect(StatId.MoveSpeed) < -0.001f;
            var flash = flashSeconds > 0f ? flashStrength * (flashRemaining / flashSeconds) : 0f;

            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                {
                    continue;
                }

                var colour = dead ? deadColor : restColours[i];

                if (slowed)
                {
                    colour = Color.Lerp(colour, slowedColor, 0.65f);
                }

                if (flash > 0f)
                {
                    colour = Color.Lerp(colour, hitColor, flash);
                }

                block.SetColor(BaseColor, colour);
                renderers[i].SetPropertyBlock(block);
            }

            DrawShield(dead);
        }

        void DrawShield(bool dead)
        {
            if (lineMaterial == null)
            {
                return;
            }

            if (dead || health.Shield <= 0f)
            {
                shieldRing?.Hide();

                return;
            }

            shieldRing ??= new GroundOutline(transform, lineMaterial);
            shieldRing.Circle(transform.position, shieldRadius, Feet());
            shieldRing.Show(shieldColor, shieldWidth);
        }

        float Feet()
        {
            return renderers.Length > 0 && renderers[0] != null
                ? renderers[0].bounds.min.y
                : transform.position.y;
        }
    }
}
