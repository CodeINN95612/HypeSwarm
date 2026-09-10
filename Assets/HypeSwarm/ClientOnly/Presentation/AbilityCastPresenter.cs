using System.Collections.Generic;
using HypeSwarm.Shared.Abilities;
using HypeSwarm.Shared.Abilities.Steps;
using HypeSwarm.Shared.Combat;
using UnityEngine;

namespace HypeSwarm.ClientOnly.Presentation
{
    /// <summary>
    /// Draws what a champion's abilities are doing: the shape a cast fills, the telegraph of a wind-up,
    /// the ring a channel grows, and the reach of the passive.
    /// </summary>
    /// <remarks>
    /// <b>The presentation half of §12.</b> Effect steps never draw anything; the runner announces that a
    /// cast began and landed, and this listens. That is why the same component shows your own cast the
    /// instant the key goes down and shows the other four players' casts when the host says so — the
    /// runner raises the same two events on every machine, once each, and this cannot tell the
    /// difference and does not need to.
    ///
    /// <para><b>Shapes come from the authored targeting steps, not from anything in here.</b> A cone is
    /// drawn because the ability's <see cref="SelectTargetsStep"/> is a cone of that radius and angle; a
    /// new ability authored in the inspector draws correctly without this file changing. The telegraph is
    /// therefore always the real hit area, which is the property a telegraph must have.</para>
    ///
    /// <para>Placeholder by design: outlines on the ground, no particles, no art. The art pass replaces
    /// the drawing; the events it listens to stay.</para>
    /// </remarks>
    [AddComponentMenu("Hype Swarm/Ability Cast Presenter")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AbilityRunner))]
    public sealed class AbilityCastPresenter : MonoBehaviour
    {
        /// <summary>Where a channel's ring starts, so a channel is visible from its first frame.</summary>
        const float ChannelStartRadius = 0.4f;

        [SerializeField]
        [Tooltip("Unlit material the outlines are drawn with. Tinted per shape, so one white one does.")]
        Material lineMaterial;

        [Header("Colours")]
        [SerializeField]
        [Tooltip("Shapes that hit enemies.")]
        Color hostileColor = new Color(1f, 0.62f, 0.2f);

        [SerializeField]
        [Tooltip("Shapes that look for allies or the caster.")]
        Color alliedColor = new Color(0.45f, 0.85f, 1f);

        [SerializeField]
        [Tooltip("The passive's reach, drawn continuously.")]
        Color auraColor = new Color(0.75f, 0.3f, 0.08f);

        [Header("Lines")]
        [SerializeField]
        [Tooltip("Seconds a landed shape takes to fade.")]
        float flashSeconds = 0.3f;

        [SerializeField]
        float telegraphWidth = 0.05f;

        [SerializeField]
        float landedWidth = 0.2f;

        [SerializeField]
        float auraWidth = 0.035f;

        struct Flash
        {
            public GroundOutline Outline;
            public float Remaining;
        }

        readonly List<Flash> flashes = new List<Flash>();
        readonly List<GroundOutline> telegraphs = new List<GroundOutline>();
        readonly Stack<GroundOutline> pool = new Stack<GroundOutline>();
        readonly List<SelectTargetsStep> shapes = new List<SelectTargetsStep>();

        AbilityRunner runner;
        CharacterController body;
        Health health;
        SelectTargetsStep auraShape;
        GroundOutline aura;
        GroundOutline channelRing;
        AbilityInstance channelling;
        float channelElapsed;
        float channelRadius;

        void Awake()
        {
            runner = GetComponent<AbilityRunner>();
            TryGetComponent(out body);
            TryGetComponent(out health);

            auraShape = FindAura(runner.Champion);
        }

        void OnEnable()
        {
            runner.Began += OnBegan;
            runner.Landed += OnLanded;
            runner.Interrupted += OnInterrupted;
        }

        void OnDisable()
        {
            runner.Began -= OnBegan;
            runner.Landed -= OnLanded;
            runner.Interrupted -= OnInterrupted;

            ClearTelegraphs();
            EndChannel();
            aura?.Hide();

            for (var i = 0; i < flashes.Count; i++)
            {
                Release(flashes[i].Outline);
            }

            flashes.Clear();
        }

        void LateUpdate()
        {
            if (lineMaterial == null)
            {
                return;
            }

            var feet = Feet();

            DrawAura(feet);
            DrawChannel(feet);
            TickFlashes();
        }

        void OnBegan(AbilityInstance instance, AbilityAim aim)
        {
            if (lineMaterial == null || instance?.Definition == null)
            {
                return;
            }

            ClearTelegraphs();

            var definition = instance.Definition;

            if (definition.CastCost == CastCost.Channelled)
            {
                channelling = instance;
                channelElapsed = 0f;
                channelRadius = LargestRadius(definition);

                return;
            }

            // Only a wind-up gets a telegraph. An instant cast lands on this same frame, and a shape that
            // appears and is replaced inside one frame is noise rather than information.
            if (definition.CastTime <= 0f)
            {
                return;
            }

            var feet = Feet();

            foreach (var shape in ShapesOf(definition))
            {
                var outline = Take();

                Draw(outline, shape, aim, feet);
                outline.Show(Color.Lerp(ColourFor(shape), Color.black, 0.45f), telegraphWidth);

                telegraphs.Add(outline);
            }
        }

        void OnLanded(AbilityInstance instance, AbilityAim aim, float channelDuration)
        {
            if (lineMaterial == null || instance?.Definition == null)
            {
                return;
            }

            if (channelling == instance)
            {
                EndChannel();
            }

            ClearTelegraphs();

            var feet = Feet();

            foreach (var shape in ShapesOf(instance.Definition))
            {
                var outline = Take();

                Draw(outline, shape, aim, feet);
                outline.Show(ColourFor(shape), landedWidth);

                flashes.Add(new Flash { Outline = outline, Remaining = flashSeconds });
            }
        }

        void OnInterrupted(AbilityInstance instance)
        {
            if (channelling == instance)
            {
                EndChannel();
            }

            ClearTelegraphs();
        }

        void Draw(GroundOutline outline, SelectTargetsStep shape, AbilityAim aim, float feet)
        {
            // The same centre and direction rules the step itself uses, so the drawing is the hit area.
            var centre = shape.Origin == ShapeOrigin.AimPoint ? aim.Point : transform.position;
            var direction = aim.HasDirection ? aim.Direction : Vector2.up;

            outline.Cone(centre, direction, shape.Radius, shape.ConeDegrees, feet);
        }

        /// <summary>
        /// The passive's reach, always. A passive has no input and no cast, so without this the player has
        /// no way to see how close something has to be before it starts burning.
        /// </summary>
        void DrawAura(float feet)
        {
            if (auraShape == null)
            {
                return;
            }

            aura ??= new GroundOutline(transform, lineMaterial);

            if (health != null && health.IsDead)
            {
                aura.Hide();

                return;
            }

            aura.Cone(transform.position, Vector2.up, auraShape.Radius, auraShape.ConeDegrees, feet);
            aura.Show(auraColor, auraWidth);
        }

        /// <summary>
        /// A ring that grows toward the ability's reach while the channel is held — the commitment made
        /// visible, to the player holding it and to everyone standing near them (§5.5.4).
        /// </summary>
        void DrawChannel(float feet)
        {
            if (channelling == null)
            {
                return;
            }

            var longest = channelling.Definition.MaxChannelDuration;

            channelElapsed += Time.deltaTime;

            // A landing that never arrived — a missed message, a champion destroyed mid-channel — must not
            // leave a ring on the floor forever.
            if (longest > 0f && channelElapsed > longest + 1f)
            {
                EndChannel();

                return;
            }

            var progress = longest > 0f ? Mathf.Clamp01(channelElapsed / longest) : 1f;

            channelRing ??= new GroundOutline(transform, lineMaterial);
            channelRing.Circle(transform.position, Mathf.Lerp(ChannelStartRadius, channelRadius, progress), feet);
            channelRing.Show(hostileColor, Mathf.Lerp(telegraphWidth, landedWidth, progress));
        }

        void TickFlashes()
        {
            for (var i = flashes.Count - 1; i >= 0; i--)
            {
                var flash = flashes[i];

                flash.Remaining -= Time.deltaTime;

                if (flash.Remaining <= 0f)
                {
                    Release(flash.Outline);
                    flashes.RemoveAt(i);

                    continue;
                }

                flash.Outline.Width = landedWidth * (flash.Remaining / flashSeconds);
                flashes[i] = flash;
            }
        }

        void EndChannel()
        {
            channelling = null;
            channelRing?.Hide();
        }

        void ClearTelegraphs()
        {
            for (var i = 0; i < telegraphs.Count; i++)
            {
                Release(telegraphs[i]);
            }

            telegraphs.Clear();
        }

        GroundOutline Take()
        {
            return pool.Count > 0 ? pool.Pop() : new GroundOutline(transform, lineMaterial);
        }

        void Release(GroundOutline outline)
        {
            outline.Hide();
            pool.Push(outline);
        }

        Color ColourFor(SelectTargetsStep shape)
        {
            return shape.Wanted == TargetFaction.Enemies ? hostileColor : alliedColor;
        }

        /// <summary>Every targeting step in what the ability does when it lands.</summary>
        List<SelectTargetsStep> ShapesOf(IAbilityDefinition definition)
        {
            shapes.Clear();

            var steps = definition.Steps;

            for (var i = 0; steps != null && i < steps.Count; i++)
            {
                if (steps[i] is SelectTargetsStep select && select.Radius > 0f)
                {
                    shapes.Add(select);
                }
            }

            return shapes;
        }

        float LargestRadius(IAbilityDefinition definition)
        {
            var largest = ChannelStartRadius;

            foreach (var shape in ShapesOf(definition))
            {
                largest = Mathf.Max(largest, shape.Radius);
            }

            return largest;
        }

        static SelectTargetsStep FindAura(IChampionDefinition champion)
        {
            if (champion?.Abilities == null)
            {
                return null;
            }

            foreach (var ability in champion.Abilities)
            {
                if (ability == null || ability.Role != AbilityRole.Passive || ability.Steps == null)
                {
                    continue;
                }

                foreach (var step in ability.Steps)
                {
                    if (step is SelectTargetsStep select && select.Origin == ShapeOrigin.Caster && select.Radius > 0f)
                    {
                        return select;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Where the champion's feet are. Shapes are drawn there rather than at the transform, which sits at
        /// the middle of the capsule — and read off the controller rather than a raycast, because this runs
        /// every frame for every champion and needs no physics to answer.
        /// </summary>
        float Feet()
        {
            return body != null
                ? transform.position.y + body.center.y - body.height * 0.5f
                : transform.position.y;
        }
    }
}
