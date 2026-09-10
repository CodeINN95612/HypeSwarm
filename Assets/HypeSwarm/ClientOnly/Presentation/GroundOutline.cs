using HypeSwarm.Shared.Movement;
using UnityEngine;
using UnityEngine.Rendering;

namespace HypeSwarm.ClientOnly.Presentation
{
    /// <summary>
    /// One flat shape on the ground — a circle or a cone — drawn with a line renderer.
    /// </summary>
    /// <remarks>
    /// Placeholder presentation, and deliberately the cheapest thing that reads: outlines rather than
    /// filled decals, one opaque unlit material tinted per shape, and fading done by thinning the line
    /// rather than by alpha. There is no transparent shader to set up, nothing to author, and nothing
    /// here that survives into the art pass.
    ///
    /// <para>What it does have to get right is geometry. The shape drawn must be exactly the shape the
    /// targeting step resolves — planar, with the cone's angle as its total opening rather than its half
    /// — or the telegraph lies, and a telegraph that lies is worse than none (§5.5.4).</para>
    /// </remarks>
    sealed class GroundOutline
    {
        const int ArcSegments = 40;

        /// <summary>Metres above the ground, so the line does not z-fight the floor it is drawn on.</summary>
        const float Lift = 0.05f;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        readonly LineRenderer line;
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

        public GroundOutline(Transform parent, Material material)
        {
            var go = new GameObject("Ground Outline");
            go.transform.SetParent(parent, false);

            line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.numCornerVertices = 2;
            line.numCapVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
        }

        /// <summary>Line width in metres. Animated to fade a shape out.</summary>
        public float Width
        {
            set => line.widthMultiplier = value;
        }

        public void Show(Color color, float width)
        {
            block.SetColor(BaseColor, color);
            line.SetPropertyBlock(block);
            line.widthMultiplier = width;
            line.enabled = true;
        }

        public void Hide()
        {
            line.enabled = false;
        }

        public void Circle(Vector3 centre, float radius, float groundY)
        {
            line.loop = true;
            line.positionCount = ArcSegments;

            for (var i = 0; i < ArcSegments; i++)
            {
                var angle = i * Mathf.PI * 2f / ArcSegments;
                var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

                line.SetPosition(i, OnGround(centre, offset, groundY));
            }
        }

        /// <summary>
        /// A wedge opening along <paramref name="direction"/>. A full circle or no angle at all is drawn
        /// as a circle, matching how <c>TargetQuery</c> treats the same values.
        /// </summary>
        public void Cone(Vector3 origin, Vector2 direction, float radius, float degrees, float groundY)
        {
            if (degrees <= 0f || degrees >= 360f)
            {
                Circle(origin, radius, groundY);

                return;
            }

            var axis = direction.sqrMagnitude > 0f ? direction.normalized : Vector2.up;
            var half = degrees * 0.5f * Mathf.Deg2Rad;

            // The apex, the arc, and a closing edge back to the apex through the loop.
            line.loop = true;
            line.positionCount = ArcSegments + 2;
            line.SetPosition(0, OnGround(origin, Vector2.zero, groundY));

            for (var i = 0; i <= ArcSegments; i++)
            {
                var angle = Mathf.Lerp(-half, half, (float)i / ArcSegments);

                line.SetPosition(i + 1, OnGround(origin, Rotate(axis, angle) * radius, groundY));
            }
        }

        static Vector3 OnGround(Vector3 centre, Vector2 planarOffset, float groundY)
        {
            var point = centre + MotionPlane.ToWorld(planarOffset);

            point.y = groundY + Lift;

            return point;
        }

        static Vector2 Rotate(Vector2 vector, float radians)
        {
            var cos = Mathf.Cos(radians);
            var sin = Mathf.Sin(radians);

            return new Vector2(vector.x * cos - vector.y * sin, vector.x * sin + vector.y * cos);
        }
    }
}
