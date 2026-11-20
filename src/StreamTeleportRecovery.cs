using UnityEngine;

namespace ATTSmoothCamera
{
    public sealed partial class StreamCameraMod
    {
        PlayerController watchedFollowSubject, followedSubject, pendingRelocationSubject;
        LocomotionController watchedFollowLocomotion;
        Transform followedRoot, followedHead, followedEye;
        Vector3 followedRootPosition, followedHeadPosition, followedEyePosition;
        bool haveFollowPose, relocationPending;

        void WatchLocomotion(PlayerController subject)
        {
            // A loading gap must not erase the last valid subject or its queued move.
            if (subject == null || !subject.IsLocalPlayer || subject != PlayerController.Current) return;
            LocomotionController locomotion = subject.LocomotionController;
            if (watchedFollowSubject == subject && watchedFollowLocomotion == locomotion) return;
            if (watchedFollowLocomotion != null) watchedFollowLocomotion.PositionChanged -= FollowPositionChanged;
            watchedFollowSubject = subject;
            watchedFollowLocomotion = locomotion;
            if (watchedFollowLocomotion != null) watchedFollowLocomotion.PositionChanged += FollowPositionChanged;
        }

        void FollowPositionChanged(PlayerController subject, Vector3 original, Vector3 next, LocomotionFunction function)
        {
            if (subject == null || subject != watchedFollowSubject || subject != PlayerController.Current || !subject.IsLocalPlayer) return;
            switch (function) {
                case LocomotionFunction.TeleportLocomotion:
                case LocomotionFunction.Reposition:
                case LocomotionFunction.Recovery:
                case LocomotionFunction.ForcedLocomotion:
                    // Locomotion callbacks may run partway through moving the rig.
                    // Consume this only after the next valid follow pose is available.
                    relocationPending = true;
                    pendingRelocationSubject = subject;
                    break;
            }
        }

        bool UpdateFollowTarget(PlayerController current, Transform eye)
        {
            if (current == null || current != PlayerController.Current || !current.IsLocalPlayer || !current.IsAlive ||
                current.transform == null || current.Head == null || eye == null) return false;
            Transform root = current.transform;
            Transform head = current.Head;
            Vector3 rootPosition = root.position;
            Vector3 headPosition = head.position;
            Vector3 eyePosition = eye.position;
            if (!Finite(rootPosition) || !Finite(headPosition) || !Finite(eyePosition)) return false;

            float limit = Safe(snapDistance.Value, 1f, 100f, 5f);
            bool pending = relocationPending && pendingRelocationSubject == current;
            bool relocated = pending || haveFollowPose &&
                (followedSubject != current || followedRoot != root || followedHead != head || followedEye != eye ||
                 CameraMath.ShouldSnap((rootPosition - followedRootPosition).sqrMagnitude, limit, false) ||
                 CameraMath.ShouldSnap((headPosition - followedHeadPosition).sqrMagnitude, limit, false) ||
                 CameraMath.ShouldSnap((eyePosition - followedEyePosition).sqrMagnitude, limit, false));

            followedSubject = current;
            followedRoot = root; followedHead = head; followedEye = eye;
            followedRootPosition = rootPosition;
            followedHeadPosition = headPosition;
            followedEyePosition = eyePosition;
            haveFollowPose = true;
            relocationPending = false;
            pendingRelocationSubject = null;
            if (!relocated) return false;

            if (recording) {
                // The render callback may already have followed the new eye.
                // Preserve captured points without appending a cross-world endpoint.
                recording = false;
                Notice("Recording stopped after a teleport or character change. Save the captured points on Paths.");
            }
            StopPath(false);
            frozenPhoto = false;
            firstPersonHead = null;
            seeded = false;
            transitionActive = false;
            avoidanceReset = true;
            nextProbe = 0f;
            orbitAngle = 0f;
            return true;
        }

        void StopWatchingLocomotion()
        {
            if (watchedFollowLocomotion != null) watchedFollowLocomotion.PositionChanged -= FollowPositionChanged;
            watchedFollowLocomotion = null;
            watchedFollowSubject = followedSubject = pendingRelocationSubject = null;
            followedRoot = followedHead = followedEye = null;
            haveFollowPose = relocationPending = false;
        }
    }
}
