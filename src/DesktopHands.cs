using UnityEngine;
using UnityEngine.InputSystem;

namespace ATTSmoothCamera
{
    // Feeds the same nearby interaction inputs used by the game's tracked controllers.
    // The session owns which two PlayerInputs may receive these poses and inputs.
    sealed class DesktopHands
    {
        sealed class HandPose
        {
            public Vector2 Offset;
            public Vector3 Angles;
            public float ReachOffset;
            public bool Grip, Armed, Trigger, TriggerArmed;
            public Vector3 Position, Velocity, AngularVelocity;
            public Quaternion Rotation = Quaternion.identity;
        }

        readonly HandPose left = new HandPose(), right = new HandPose();
        bool active, seeded, previousToggle;
        float previousBaseReach;
        Vector3 previousHead;
        public bool IsAdjustingHand { get; private set; }

        public void Sample(bool controlsActive, bool toggleGrips, float baseReach,
            Quaternion lookRotation, Vector3 headPosition, float deltaTime)
        {
            if (!Finite(headPosition.x) || !Finite(headPosition.y) || !Finite(headPosition.z))
            {
                Clear();
                return;
            }
            float rotationLength = lookRotation.x * lookRotation.x + lookRotation.y * lookRotation.y +
                lookRotation.z * lookRotation.z + lookRotation.w * lookRotation.w;
            if (!Finite(rotationLength) || rotationLength < 0.001f) lookRotation = Quaternion.identity;
            else lookRotation = lookRotation.normalized;
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            active = controlsActive && keyboard != null && mouse != null;
            bool adjustingLeft = active && keyboard[Key.Z].isPressed;
            bool adjustingRight = active && keyboard[Key.X].isPressed;
            IsAdjustingHand = adjustingLeft || adjustingRight;
            bool rotating = active && (keyboard[Key.LeftAlt].isPressed || keyboard[Key.RightAlt].isPressed);
            float reach = Finite(baseReach) ? Mathf.Clamp(baseReach, 0.25f, 1f) : 0.55f;
            if (Mathf.Abs(previousBaseReach - reach) > 0.001f)
            {
                left.ReachOffset = right.ReachOffset = 0f;
                previousBaseReach = reach;
            }
            if (toggleGrips != previousToggle)
            {
                left.Grip = right.Grip = false;
                left.Armed = right.Armed = false;
                previousToggle = toggleGrips;
            }
            if (active && keyboard[Key.R].wasPressedThisFrame) ResetHands();
            UpdateGrip(left, active && keyboard[Key.Q].isPressed,
                active && keyboard[Key.Q].wasPressedThisFrame, toggleGrips);
            UpdateGrip(right, active && keyboard[Key.E].isPressed,
                active && keyboard[Key.E].wasPressedThisFrame, toggleGrips);
            UpdateTrigger(left, active && mouse.leftButton.isPressed);
            UpdateTrigger(right, active && mouse.rightButton.isPressed);

            if (active)
            {
                Vector2 mouseDelta = Vector2.ClampMagnitude(mouse.delta.ReadValue(), 240f);
                float wheel = Mathf.Clamp(mouse.scroll.ReadValue().y / 120f, -5f, 5f);
                if (!Finite(mouseDelta.x) || !Finite(mouseDelta.y)) mouseDelta = Vector2.zero;
                if (!Finite(wheel)) wheel = 0f;
                if (adjustingLeft) Adjust(left, mouseDelta, wheel, rotating, reach);
                if (adjustingRight) Adjust(right, mouseDelta, wheel, rotating, reach);
                if (!IsAdjustingHand && !rotating && wheel != 0f)
                {
                    AdjustReach(left, wheel, reach);
                    AdjustReach(right, wheel, reach);
                }
            }

            float dt = Finite(deltaTime) ? Mathf.Clamp(deltaTime, 0.001f, 0.1f) : 0.016f;
            bool snap = !seeded || (headPosition - previousHead).sqrMagnitude > 4f;
            Vector3 headDelta = seeded ? headPosition - previousHead : Vector3.zero;
            UpdatePose(left, true, reach, headPosition, lookRotation, headDelta, dt, snap);
            UpdatePose(right, false, reach, headPosition, lookRotation, headDelta, dt, snap);
            previousHead = headPosition;
            seeded = true;
        }

        void UpdateTrigger(HandPose hand, bool held)
        {
            if (!active)
            {
                hand.Trigger = hand.TriggerArmed = false;
                return;
            }
            if (!held) hand.TriggerArmed = true;
            hand.Trigger = held && hand.TriggerArmed;
        }

        static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }

        void UpdateGrip(HandPose hand, bool held, bool pressed, bool toggle)
        {
            if (!active)
            {
                hand.Grip = false;
                hand.Armed = false;
                return;
            }
            if (!held) hand.Armed = true;
            if (toggle)
            {
                if (pressed && hand.Armed) hand.Grip = !hand.Grip;
            }
            else hand.Grip = held && hand.Armed;
        }

        static void Adjust(HandPose hand, Vector2 mouse, float wheel, bool rotate, float reach)
        {
            if (rotate)
            {
                hand.Angles.x = Mathf.Clamp(hand.Angles.x - mouse.y * 0.35f, -100f, 100f);
                hand.Angles.y = Mathf.Clamp(hand.Angles.y + mouse.x * 0.35f, -100f, 100f);
                hand.Angles.z = Mathf.Repeat(hand.Angles.z + wheel * 10f + 180f, 360f) - 180f;
            }
            else
            {
                hand.Offset.x = Mathf.Clamp(hand.Offset.x + mouse.x * 0.002f, -0.4f, 0.4f);
                hand.Offset.y = Mathf.Clamp(hand.Offset.y + mouse.y * 0.002f, -0.5f, 0.6f);
                AdjustReach(hand, wheel, reach);
            }
        }

        static void AdjustReach(HandPose hand, float wheel, float reach)
        {
            hand.ReachOffset = Mathf.Clamp(hand.ReachOffset + wheel * 0.05f, 0.25f - reach, 1f - reach);
        }

        void UpdatePose(HandPose hand, bool isLeft, float reach, Vector3 head,
            Quaternion look, Vector3 headDelta, float dt, bool snap)
        {
            Vector3 offset = new Vector3((isLeft ? -0.22f : 0.22f) + hand.Offset.x,
                -0.30f + hand.Offset.y, Mathf.Clamp(reach + hand.ReachOffset, 0.25f, 1f));
            Vector3 targetPosition = head + look * Vector3.ClampMagnitude(offset, 1.12f);
            Quaternion targetRotation = look * Quaternion.Euler(hand.Angles);
            Vector3 oldPosition = hand.Position;
            Quaternion oldRotation = hand.Rotation;
            float blend = 1f - Mathf.Pow(0.5f, dt / 0.025f);
            // Carry the hands with locomotion, then smooth only the relative aiming pose.
            hand.Position = snap ? targetPosition : Vector3.Lerp(hand.Position + headDelta, targetPosition, blend);
            hand.Rotation = snap ? targetRotation : Quaternion.Slerp(hand.Rotation, targetRotation, blend);
            hand.Velocity = active && !snap
                ? Vector3.ClampMagnitude((hand.Position - oldPosition - headDelta) / dt, 10f)
                : Vector3.zero;
            hand.AngularVelocity = Vector3.zero;
            if (active && !snap)
            {
                Quaternion change = hand.Rotation * Quaternion.Inverse(oldRotation);
                float angle;
                Vector3 axis;
                change.ToAngleAxis(out angle, out axis);
                if (angle > 180f) angle -= 360f;
                if (Finite(angle) && Finite(axis.x) && Finite(axis.y) && Finite(axis.z) && axis.sqrMagnitude > 0.001f)
                    hand.AngularVelocity = Vector3.ClampMagnitude(axis.normalized * angle * Mathf.Deg2Rad / dt, 20f);
            }
        }

        public void Fill(PlayerInput handInput, ControllerInput target, bool isLeft, Vector2 moveStick, bool run)
        {
            if (target == null) return;
            HandPose hand = isLeft ? left : right;
            bool grip = active && hand.Grip;
            bool trigger = active && hand.Trigger;
            target.SmoothLocomotion = active ? Vector2.ClampMagnitude(moveStick, 1f) : Vector2.zero;
            target.ToggleRun.State = active && run;
            target.Grab.State = grip;
            target.HardGrab.State = grip;
            target.ToggleGrab.State = false;
            target.ReleaseToggleGrab.State = false;
            target.GrabAssist.State = false;
            target.MetaMenu.State = false;
            target.ToggleMetaMenu.State = false;
            target.Teleport.State = false;
            target.GrabAxis = grip ? 1f : 0f;
            target.GrabStrength = grip ? 1f : 0f;
            target.SecondaryAxis = trigger ? 1f : 0f;
            if (handInput != null && handInput.Player != null && handInput.Player.PlayerTransform != null)
            {
                Quaternion inverse = Quaternion.Inverse(handInput.Player.PlayerTransform.rotation);
                target.CalculateAndSetVelocities(handInput.Player, inverse * hand.Velocity, inverse * hand.AngularVelocity);
            }
        }

        public void ApplyPose(PlayerInput handInput, bool isLeft)
        {
            if (!seeded || handInput == null || handInput.TargetTransform == null) return;
            HandPose hand = isLeft ? left : right;
            handInput.TargetTransform.SetPositionAndRotation(hand.Position, hand.Rotation);
        }

        public void ResetHands()
        {
            left.Offset = right.Offset = Vector2.zero;
            left.Angles = right.Angles = Vector3.zero;
            left.ReachOffset = right.ReachOffset = 0f;
            seeded = false;
        }

        public void ReleaseItems(PlayerController player)
        {
            left.Grip = right.Grip = false;
            left.Armed = right.Armed = false;
            left.Trigger = right.Trigger = false;
            left.TriggerArmed = right.TriggerArmed = false;
            if (player == null || player != PlayerController.Current || !player.IsLocalPlayer) return;
            Release(player.LeftController, player);
            Release(player.RightController, player);
        }

        static void Release(Controller controller, PlayerController player)
        {
            if (controller == null || controller.PlayerController != player) return;
            PlayerInput input = controller.PlayerInput;
            if (input != null && input.RawInput != null)
                input.RawInput.SetAndCallEvents(input, new ControllerInput());
            Interactor interactor = controller.Interactor;
            if (interactor != null && interactor.IsInteracting) interactor.StopInteract(false, false);
        }

        public void Clear()
        {
            active = seeded = IsAdjustingHand = false;
            left.Grip = right.Grip = false;
            left.Armed = right.Armed = false;
            left.Trigger = right.Trigger = false;
            left.TriggerArmed = right.TriggerArmed = false;
            left.Velocity = right.Velocity = Vector3.zero;
            left.AngularVelocity = right.AngularVelocity = Vector3.zero;
        }
    }
}
