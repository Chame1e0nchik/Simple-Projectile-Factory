using System;
using UnityEngine;

namespace SimpleProjectileFactory
{
    [Serializable]
    [ProjectileModule("Direct Shot", "Travels in a straight line along the launch direction. Supports homing.")]
    public class DirectShotMovement : MovementModule, ISteerable
    {
        private Vector3 direction;

        public override void Initialize(Projectile p)
        {
            Vector3 d = p.Launch.Direction;
            direction = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
            Heading = direction;
        }

        public override Vector3 Step(Projectile p, float speed, float dt)
        {
            return direction * (speed * dt);
        }

        public void Steer(Vector3 desiredDirection, float maxRadians)
        {
            direction = Vector3.RotateTowards(direction, desiredDirection, maxRadians, 0f);
            Heading = direction;
        }
    }

    [Serializable]
    [ProjectileModule("Ballistic Shot", "Follows a gravity-affected arc. Can solve the launch angle to land on the aim point.")]
    public class BallisticMovement : MovementModule
    {
        [Tooltip("Solve the launch angle so the arc lands on the aim point (falls back to a plain launch when no aim point is given). " +
                 "When off, the projectile is simply launched along the direction and gravity bends it.")]
        public bool aimAtTarget = true;

        [Tooltip("Use the high lobbing arc instead of the flat one (only when Aim At Target is on).")]
        public bool highArc;

        [Tooltip("Extra upward pitch in degrees (only when Aim At Target is off).")]
        public float launchPitchDegrees;

        [Tooltip("Multiplier on Physics.gravity.")]
        public float gravityScale = 1f;

        private Vector3 velocity;
        private Vector3 gravity;

        public override void Initialize(Projectile p)
        {
            gravity = Physics.gravity * gravityScale;
            float speed = p.BaseSpeed;
            Vector3 dir = p.Launch.Direction;
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
            dir.Normalize();

            if (!(aimAtTarget && p.Launch.HasTargetPoint &&
                  TrySolveArc(p.Position, p.Launch.TargetPoint, speed, gravity, highArc, out velocity)))
            {
                if (Mathf.Abs(launchPitchDegrees) > 0.001f)
                {
                    Vector3 right = Vector3.Cross(Vector3.up, dir);
                    if (right.sqrMagnitude > 1e-6f)
                        dir = Quaternion.AngleAxis(-launchPitchDegrees, right.normalized) * dir;
                }
                velocity = dir * speed;
            }

            Heading = velocity.sqrMagnitude > 1e-6f ? velocity.normalized : dir;
        }

        public override Vector3 Step(Projectile p, float speed, float dt)
        {
            // Speed modifiers dilate time along the same arc: the path keeps its shape,
            // the projectile just travels it faster or slower.
            float baseSpeed = p.BaseSpeed;
            float t = baseSpeed > 1e-4f ? dt * (speed / baseSpeed) : dt;

            Vector3 displacement = velocity * t + gravity * (0.5f * t * t);
            velocity += gravity * t;

            if (velocity.sqrMagnitude > 1e-4f) Heading = velocity.normalized;
            return displacement;
        }

        public override void Validate(ProjectileValidationContext ctx)
        {
            if (gravityScale <= 0f)
                ctx.Warning("Gravity Scale is 0 or negative: the shot will not arc downwards.");
        }

        // Classic projectile-motion solution, gravity assumed along -Y.
        private static bool TrySolveArc(Vector3 from, Vector3 to, float speed, Vector3 g, bool high, out Vector3 velocity)
        {
            velocity = Vector3.zero;
            float G = -g.y;
            if (G <= 1e-4f || speed <= 0f) return false;

            Vector3 delta = to - from;
            Vector3 flat = new Vector3(delta.x, 0f, delta.z);
            float d = flat.magnitude;
            if (d < 1e-3f) return false;

            float v2 = speed * speed;
            float disc = v2 * v2 - G * (G * d * d + 2f * delta.y * v2);

            float tan;
            if (disc < 0f)
                tan = 1f; // out of range: best effort at maximum-range angle
            else
            {
                float root = Mathf.Sqrt(disc);
                tan = (v2 + (high ? root : -root)) / (G * d);
            }

            float angle = Mathf.Atan(tan);
            velocity = (flat / d) * (speed * Mathf.Cos(angle)) + Vector3.up * (speed * Mathf.Sin(angle));
            return true;
        }
    }
}
