using System;
using System.Collections.Generic;
using UnityEngine;

namespace ATTSmoothCamera
{
    // Owns only spectator-camera placement. It never moves the player or any obstacle.
    internal sealed class CameraAvoidance
    {
        const float Radius = 0.16f;
        const float Skin = 0.05f;
        const float RecoveryHalfLife = 0.2f;
        static readonly float[] Angles = { 0f, 20f, -20f, 40f, -40f, 65f, -65f };
        static readonly float[] Rises = { 0f, 0.35f, 0.7f };
        readonly RaycastHit[] castBuffer = new RaycastHit[64];
        readonly Collider[] overlapBuffer = new Collider[64];
        readonly Dictionary<Collider, bool> ignoreCache = new Dictionary<Collider, bool>(64);
        PlayerController subject;
        bool ready;
        float distanceFraction = 1f;
        int selectedChoice;

        public Vector3 Resolve(PlayerController subject, Vector3 pivot, Vector3 desired, float dt, bool reset)
        {
            Prepare(subject);
            if (!Finite(pivot)) return Vector3.zero;
            if (!Finite(desired)) return pivot;
            Vector3 original = desired - pivot;
            float length = original.magnitude;
            if (!Finite(length) || length < 0.001f) { Reset(); return pivot; }
            if (reset) Reset();
            if (PivotBlocked(pivot)) {
                distanceFraction = 0f;
                ready = true;
                return pivot;
            }

            float originalClear = ClearDistance(pivot, original / length, length);
            float originalFraction = originalClear / length;
            Vector3 chosenDirection = original / length;
            float chosenLength = length;
            float chosenClear = originalClear;
            int chosen = 0;
            // Retain an alternative until the original route is mostly clear again.
            // This gap prevents choosing a different side at every corner/frame.
            float obstructionThreshold = selectedChoice == 0 ? 0.8f : 0.95f;
            if (originalFraction < obstructionThreshold) {
                float bestScore = Score(0, 0f, 0f, originalFraction);
                for (int angleIndex = 0; angleIndex < Angles.Length; angleIndex++) {
                    for (int riseIndex = 0; riseIndex < Rises.Length; riseIndex++) {
                        int choice = angleIndex * Rises.Length + riseIndex;
                        if (choice == 0) continue;
                        float angle = Angles[angleIndex];
                        float rise = Rises[riseIndex];
                        Vector3 candidate = Quaternion.AngleAxis(angle, Vector3.up) * original + Vector3.up * rise;
                        float candidateLength = candidate.magnitude;
                        if (candidateLength < 0.001f) continue;
                        Vector3 direction = candidate / candidateLength;
                        float clear = ClearDistance(pivot, direction, candidateLength);
                        float score = Score(choice, angle, rise, clear / candidateLength);
                        if (score <= bestScore) continue;
                        bestScore = score;
                        chosenDirection = direction;
                        chosenLength = candidateLength;
                        chosenClear = clear;
                        chosen = choice;
                    }
                }
            }
            selectedChoice = chosen;
            float allowedFraction = Mathf.Clamp01(chosenClear / chosenLength);
            if (!ready || reset || allowedFraction < distanceFraction) distanceFraction = allowedFraction;
            else distanceFraction = Mathf.Lerp(distanceFraction, allowedFraction, CameraMath.Blend(RecoveryHalfLife, dt));
            ready = true;
            return pivot + chosenDirection * (chosenLength * Mathf.Min(distanceFraction, allowedFraction));
        }

        // Call after the main position interpolation: a safe desired endpoint does
        // not make the interpolated route around a corner safe.
        public Vector3 Clamp(PlayerController subject, Vector3 pivot, Vector3 position)
        {
            Prepare(subject);
            if (!Finite(pivot)) return Vector3.zero;
            if (!Finite(position) || PivotBlocked(pivot)) return pivot;
            Vector3 delta = position - pivot;
            float length = delta.magnitude;
            if (!Finite(length) || length < 0.001f) return pivot;
            Vector3 direction = delta / length;
            return pivot + direction * ClearDistance(pivot, direction, length);
        }

        void Reset()
        {
            ready = false;
            distanceFraction = 1f;
            selectedChoice = 0;
        }

        void Prepare(PlayerController player)
        {
            subject = player;
            ignoreCache.Clear();
        }

        float Score(int choice, float angle, float rise, float clearance)
        {
            float score = clearance - Mathf.Abs(angle) * 0.0018f - rise * 0.05f;
            if (choice == selectedChoice) score += 0.08f;
            if (selectedChoice != 0) {
                float previousAngle = Angles[selectedChoice / Rises.Length];
                if (previousAngle * angle < 0f) score -= 0.04f;
            }
            return score;
        }

        float ClearDistance(Vector3 origin, Vector3 direction, float length)
        {
            int count = Physics.SphereCastNonAlloc(origin, Radius, direction, castBuffer, length,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            // NonAlloc does not promise which hits are kept when full. A rare
            // overflow therefore needs the complete set before choosing nearest.
            RaycastHit[] hits = castBuffer;
            if (count >= castBuffer.Length) {
                hits = Physics.SphereCastAll(origin, Radius, direction, length,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                count = hits.Length;
            }
            float nearest = length;
            for (int i = 0; i < count; i++) {
                Collider collider = hits[i].collider;
                if (Ignored(collider)) continue;
                nearest = Mathf.Min(nearest, Mathf.Max(0f, hits[i].distance - Skin));
            }
            return nearest;
        }

        bool PivotBlocked(Vector3 pivot)
        {
            int count = Physics.OverlapSphereNonAlloc(pivot, Radius, overlapBuffer,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Collider[] overlaps = overlapBuffer;
            if (count >= overlapBuffer.Length) {
                overlaps = Physics.OverlapSphere(pivot, Radius,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                count = overlaps.Length;
            }
            for (int i = 0; i < count; i++) if (!Ignored(overlaps[i])) return true;
            return false;
        }

        bool Ignored(Collider collider)
        {
            if (collider == null || collider.isTrigger) return true;
            bool ignored;
            if (ignoreCache.TryGetValue(collider, out ignored)) return ignored;
            PlayerController owner = collider.GetComponentInParent<PlayerController>();
            ignored = owner != null && (owner == subject || owner.IsLocalPlayer);
            // Tracked hands are separate network entities, not necessarily
            // children of the embodied PlayerController. Their kinematic
            // colliders must not obstruct the crafting camera's hand pivot.
            if (!ignored) {
                Controller hand = collider.GetComponentInParent<Controller>();
                ignored = hand != null && ((subject != null &&
                    (hand == subject.LeftController || hand == subject.RightController || hand.PlayerController == subject)) ||
                    (hand.PlayerController != null && hand.PlayerController.IsLocalPlayer));
            }
            Rigidbody body = collider.attachedRigidbody;
            if (body != null && !body.isKinematic) ignored = true;
            ignoreCache[collider] = ignored;
            return ignored;
        }

        static bool Finite(Vector3 value) { return Finite(value.x) && Finite(value.y) && Finite(value.z); }
        static bool Finite(float value) { return !Single.IsNaN(value) && !Single.IsInfinity(value); }
    }
}
