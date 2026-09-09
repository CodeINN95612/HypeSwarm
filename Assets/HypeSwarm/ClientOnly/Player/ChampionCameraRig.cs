using UnityEngine;

namespace HypeSwarm.ClientOnly.Player
{
    /// <summary>
    /// Frames one champion: a fixed angle, a smoothed follow, and a bounded bias toward whatever the
    /// player is aiming at.
    /// </summary>
    /// <remarks>
    /// Angle and distance are authored rather than derived, and the position is rebuilt from them
    /// every frame, so the rig can be re-aimed in play mode and stays consistent with what the
    /// aiming code assumes about the ground plane.
    ///
    /// <para>Runs in <c>LateUpdate</c>, after <see cref="ChampionController"/> has moved. Following a
    /// position from the previous frame is a jitter that is easy to mistake for a smoothing problem
    /// and impossible to smooth away.</para>
    /// </remarks>
    [RequireComponent(typeof(Camera))]
    public sealed class ChampionCameraRig : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Champion to follow. Assigned at runtime by ChampionOwnership — five champions " +
                 "share a scene and only one of them is ours.")]
        ChampionController target;

        [Header("Framing")]
        [SerializeField]
        [Tooltip("Downward angle. Higher reads more like a map and less like a world; lower hides " +
                 "what is behind obstacles.")]
        [Range(20f, 89f)]
        float pitch = 55f;

        [SerializeField]
        [Tooltip("Rotation around the character. Movement input is rotated to match, so the " +
                 "controls stay relative to what the player sees.")]
        float yaw;

        [SerializeField]
        [Tooltip("Metres from the focus point to the camera. This is the single number that decides " +
                 "how much horde a player can see coming.")]
        float distance = 18f;

        [Header("Follow")]
        [SerializeField]
        [Tooltip("Seconds for the camera to close most of the distance to the character. Small " +
                 "values are rigid, large ones lag behind a dash.")]
        float followSmoothTime = 0.12f;

        [SerializeField]
        [Tooltip("Fraction of the distance to the cursor the view shifts toward it.")]
        [Range(0f, 1f)]
        float aimLeadFraction = 0.25f;

        [SerializeField]
        [Tooltip("Hard cap on that shift, in metres. The cap is what keeps the character on screen " +
                 "when the cursor is flicked to the edge.")]
        float aimLeadMaxDistance = 5f;

        [SerializeField]
        [Tooltip("Seconds for the aim bias to settle. Slower than the follow on purpose — the view " +
                 "should not twitch with the cursor.")]
        float aimLeadSmoothTime = 0.25f;

        Vector3 focus;
        Vector3 focusVelocity;
        Vector3 lead;
        Vector3 leadVelocity;

        /// <summary>
        /// Follows a different champion, framing it immediately.
        /// </summary>
        /// <remarks>
        /// Pushed in rather than searched for. With five champions in the scene there is no query
        /// that picks the right one — <see cref="ChampionOwnership"/> is the only thing that knows,
        /// and it knows the moment authority arrives.
        /// </remarks>
        public void Follow(ChampionController champion)
        {
            target = champion;
            SnapToTarget();
        }

        void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            var desiredLead = target.HasAimPoint
                ? AimGeometry.LeadOffset(
                    target.transform.position, target.AimPoint, aimLeadFraction, aimLeadMaxDistance)
                : Vector3.zero;

            lead = Vector3.SmoothDamp(lead, desiredLead, ref leadVelocity, aimLeadSmoothTime);
            focus = Vector3.SmoothDamp(
                focus, target.transform.position + lead, ref focusVelocity, followSmoothTime);

            ApplyFraming();
        }

        /// <summary>Places the camera without smoothing. For spawns, respawns, and teleports.</summary>
        public void SnapToTarget()
        {
            if (target == null)
            {
                return;
            }

            lead = Vector3.zero;
            leadVelocity = Vector3.zero;
            focusVelocity = Vector3.zero;
            focus = target.transform.position;

            ApplyFraming();
        }

        void ApplyFraming()
        {
            transform.SetPositionAndRotation(
                AimGeometry.BoomPosition(focus, pitch, yaw, distance),
                Quaternion.Euler(pitch, yaw, 0f));
        }
    }
}
