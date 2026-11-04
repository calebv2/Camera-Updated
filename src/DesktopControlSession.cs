using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using Alta.Character;

namespace ATTSmoothCamera
{
    // Supplies ordinary local controller input; the game's locomotion retains collision,
    // gravity, stamina, damage and network movement handling.
    public sealed class DesktopControlSession : IDisposable
    {
        static DesktopControlSession current;
        readonly HarmonyLib.Harmony harmony;
        readonly List<MethodBase> patched = new List<MethodBase>();
        readonly DesktopHands hands = new DesktopHands();
        readonly ControllerInput leftState = new ControllerInput(), rightState = new ControllerInput();
        readonly List<PoseSnapshot> savedPoses = new List<PoseSnapshot>();
        PlayerCharacter player;
        SmoothLocomotion locomotion;
        PlayerInput left, right;
        Transform head, cameraHead, leftTarget, rightTarget;
        bool requested, captured, blocked, readyToCapture, disposed, smoothBefore, cursorOwned, available;
        CursorLockMode cursorBefore;
        bool cursorVisibleBefore;
        float yaw, pitch, headHeight;
        int sampleFrame = -1, captureFrame = -1;
        Vector2 movement;
        bool running;
        string status = "Off. Enable desktop gameplay to use keyboard and mouse.";

        public float Sensitivity { get; set; }
        public bool InvertY { get; set; }
        public float StandingHeight { get; set; }
        public float CrouchHeight { get; set; }
        public float HandReach { get; set; }
        public bool ToggleGrips { get; set; }
        public bool Requested { get { return requested; } }
        public bool Captured { get { return captured; } }
        public string Status { get { return status; } }

        public DesktopControlSession(HarmonyLib.Harmony owner)
        {
            if (owner == null) throw new ArgumentNullException("owner");
            if (current != null) throw new InvalidOperationException("A desktop control session already exists.");
            harmony = owner;
            Sensitivity = .12f; StandingHeight = 1.65f; CrouchHeight = 1.05f;
            HandReach = .55f; ToggleGrips = true;
            current = this;
            try {
                Patch(typeof(PlayerInput), "Update", "InputUpdatePrefix", null);
                Patch(typeof(PlayerInput), "UpdatePosition", "InputPositionPrefix", null);
                Patch(typeof(Alta.XR.VRTrackedDevice), "UpdatePosition", "TrackedPositionPrefix", null);
                Patch(typeof(SmoothLocomotion), "ShouldIgnoreInput", null, "ProviderTranspiler");
                Patch(typeof(SmoothLocomotion), "ApplyMovement", null, "ProviderTranspiler");
                Patch(typeof(SmoothLocomotion), "CheckInput", null, "DirectionTranspiler");
                available = true;
            } catch (Exception error) {
                foreach (MethodBase method in patched) harmony.Unpatch(method, HarmonyPatchType.All, harmony.Id);
                patched.Clear();
                status = "Desktop gameplay is unavailable for this game build: " + error.GetType().Name;
                MelonLoader.MelonLogger.Warning(status + ": " + error.Message);
            }
        }

        void Patch(Type type, string name, string prefix, string transpiler)
        {
            MethodInfo target = AccessTools.DeclaredMethod(type, name);
            if (target == null) throw new MissingMethodException(type.FullName, name);
            patched.Add(target);
            harmony.Patch(target,
                prefix == null ? null : new HarmonyMethod(typeof(DesktopControlSession), prefix),
                null,
                transpiler == null ? null : new HarmonyMethod(typeof(DesktopControlSession), transpiler));
        }

        public void SetRequested(bool value)
        {
            if (disposed || requested == value) return;
            if (value && !available) return;
            if (value && OldDesktopModPresent()) { status = "Disable FlatscreenATTMod.dll and restart the game before enabling desktop gameplay here."; return; }
            requested = value;
            if (!value) { Detach(); status = "Off. VR controls restored."; }
            else { readyToCapture = false; status = "Waiting for your living character..."; }
        }

        static bool OldDesktopModPresent()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (assembly.GetType("FlatscreenATTMod.FlatscreenMod", false) != null) return true;
            return false;
        }

        public bool Owns(PlayerController candidate)
        {
            return requested && player != null && candidate == player && candidate == PlayerController.Current &&
                candidate.IsLocalPlayer && candidate.IsAlive && !candidate.IsDowned && !candidate.IsSpirit;
        }

        bool OwnsInput(PlayerInput input)
        {
            return Owns(player) && IsBoundInput(input);
        }

        bool IsBoundInput(PlayerInput input)
        {
            return player != null && player == PlayerController.Current && player.IsLocalPlayer && input != null &&
                (input == left && leftTarget != null && input.TargetTransform == leftTarget ||
                 input == right && rightTarget != null && input.TargetTransform == rightTarget);
        }

        bool OwnsLocomotion(SmoothLocomotion candidate)
        {
            return Owns(player) && candidate != null && candidate == locomotion;
        }

        bool ValidPlayer(PlayerController candidate)
        {
            return candidate is PlayerCharacter && candidate is SmoothLocomotionPlayerController &&
                candidate.IsLocalPlayer && candidate.IsAlive && !candidate.IsDowned && !candidate.IsSpirit &&
                candidate.Head != null && candidate.Camera != null && candidate.NetworkPlayer != null &&
                candidate.NetworkPlayer.IsLoaded && candidate.LeftController != null && candidate.RightController != null &&
                candidate.LeftController.PlayerInput != null && candidate.RightController.PlayerInput != null &&
                candidate.LeftController.PlayerInput.TargetTransform != null && candidate.RightController.PlayerInput.TargetTransform != null;
        }

        public void Update(bool controlsBlocked)
        {
            if (disposed || !requested) return;
            blocked = controlsBlocked;
            PlayerController candidate = PlayerController.Current;
            if (player != null && (!ValidPlayer(candidate) || candidate != player || BindingChanged())) Detach();
            if (player == null) {
                if (!ValidPlayer(candidate)) { status = "Waiting for a loaded, living character. Join as a player rather than Fly."; return; }
                Attach((PlayerCharacter)candidate);
            }
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            bool unavailable = blocked || !Application.isFocused || keyboard == null || mouse == null;
            bool releaseKey = keyboard != null && (keyboard[Key.Escape].isPressed || keyboard[Key.F6].wasPressedThisFrame);
            if (unavailable || releaseKey) {
                ReleaseCapture();
                status = unavailable ? "Paused. Close menus, then click the game to resume." : "Mouse released. Click the game to resume.";
            } else if (!captured) {
                if (!mouse.leftButton.isPressed) readyToCapture = true;
                if (readyToCapture && mouse.leftButton.wasPressedThisFrame) {
                    Capture();
                    status = "Desktop control active. F6 opens settings; Esc releases the mouse.";
                }
            }
            if (player != null) SampleInput();
        }

        bool BindingChanged()
        {
            return player == null || player.Head != head || player.Camera == null || player.Camera.transform != cameraHead ||
                player.LeftController == null || player.RightController == null ||
                player.LeftController.PlayerInput != left || player.RightController.PlayerInput != right ||
                left.TargetTransform != leftTarget || right.TargetTransform != rightTarget ||
                ((SmoothLocomotionPlayerController)player).SmoothLocomotion != locomotion;
        }

        void Attach(PlayerCharacter character)
        {
            player = character;
            locomotion = ((SmoothLocomotionPlayerController)character).SmoothLocomotion;
            if (locomotion == null) { player = null; status = "This character has no smooth locomotion controller."; return; }
            left = character.LeftController.PlayerInput; right = character.RightController.PlayerInput;
            leftTarget = left.TargetTransform; rightTarget = right.TargetTransform;
            head = character.Head; cameraHead = character.Camera.transform;
            SavePose(head); SavePose(cameraHead); SavePose(left.TargetTransform); SavePose(right.TargetTransform);
            Vector3 angles = head.rotation.eulerAngles;
            yaw = angles.y; pitch = NormalizePitch(angles.x);
            headHeight = Mathf.Clamp(Safe(StandingHeight, 1.65f), .8f, 2.2f);
            smoothBefore = locomotion.IsEnabled;
            locomotion.IsEnabled = true;
            cursorBefore = Cursor.lockState; cursorVisibleBefore = Cursor.visible; cursorOwned = true;
            captured = false; readyToCapture = false; sampleFrame = -1; captureFrame = -1;
            hands.Clear();
            ApplyHeadPose();
            status = "Ready. Close settings, then click the game to control your character.";
        }

        void Capture()
        {
            captured = true; readyToCapture = false; captureFrame = Time.frameCount;
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        }

        void ReleaseCapture()
        {
            bool wasCaptured = captured;
            captured = false; readyToCapture = false; movement = Vector2.zero; running = false;
            sampleFrame = -1;
            if (cursorOwned) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            if (player != null) {
                hands.Sample(false, ToggleGrips, HandReach, Quaternion.Euler(pitch, yaw, 0f), HeadPosition(), 0f);
                WriteInput(left); WriteInput(right);
                if (wasCaptured) hands.ReleaseItems(player);
            }
        }

        bool ControlsActive()
        {
            Keyboard keyboard = Keyboard.current;
            return captured && !blocked && Application.isFocused && Owns(player) && keyboard != null && Mouse.current != null &&
                !keyboard[Key.Escape].isPressed && !keyboard[Key.F6].wasPressedThisFrame;
        }

        void SampleInput()
        {
            if (player == null) return;
            if (sampleFrame == Time.frameCount) {
                // A focus/menu transition may happen after this frame's first native input update.
                if (!ControlsActive()) NeutralHands();
                return;
            }
            sampleFrame = Time.frameCount;
            Keyboard keyboard = Keyboard.current;
            bool active = ControlsActive() && captureFrame != Time.frameCount;
            movement = Vector2.zero; running = false;
            if (active) {
                movement = new Vector2((keyboard[Key.D].isPressed ? 1f : 0f) - (keyboard[Key.A].isPressed ? 1f : 0f),
                    (keyboard[Key.W].isPressed ? 1f : 0f) - (keyboard[Key.S].isPressed ? 1f : 0f));
                movement = Vector2.ClampMagnitude(movement, 1f);
                running = keyboard[Key.LeftShift].isPressed || keyboard[Key.RightShift].isPressed;
                if (!keyboard[Key.Z].isPressed && !keyboard[Key.X].isPressed) {
                    Vector2 delta = Mouse.current.delta.ReadValue();
                    float sensitivity = Mathf.Clamp(Safe(Sensitivity, .12f), .01f, 1f);
                    yaw = Mathf.Repeat(yaw + Mathf.Clamp(Safe(delta.x, 0f), -1000f, 1000f) * sensitivity, 360f);
                    pitch = Mathf.Clamp(pitch + Mathf.Clamp(Safe(delta.y, 0f), -1000f, 1000f) * sensitivity * (InvertY ? 1f : -1f), -85f, 85f);
                }
                float standing = Mathf.Clamp(Safe(StandingHeight, 1.65f), .8f, 2.2f);
                float targetHeight = keyboard[Key.LeftCtrl].isPressed || keyboard[Key.RightCtrl].isPressed
                    ? Mathf.Min(standing, Mathf.Clamp(Safe(CrouchHeight, 1.05f), .6f, 1.6f)) : standing;
                headHeight = Mathf.MoveTowards(headHeight, targetHeight, Mathf.Clamp(Time.unscaledDeltaTime, 0f, .1f) * 3f);
            }
            ApplyHeadPose();
            hands.Sample(active, ToggleGrips, Mathf.Clamp(Safe(HandReach, .55f), .25f, .9f), Quaternion.Euler(pitch, yaw, 0f),
                HeadPosition(), Mathf.Clamp(Time.unscaledDeltaTime, 0f, .1f));
        }

        Vector3 HeadPosition()
        {
            return player == null ? Vector3.zero : player.transform.TransformPoint(new Vector3(0f, headHeight, 0f));
        }

        void ApplyHeadPose()
        {
            if (!Owns(player)) return;
            Vector3 position = HeadPosition();
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            if (head != null) { head.position = position; head.rotation = rotation; }
            if (cameraHead != null && cameraHead != head) { cameraHead.position = position; cameraHead.rotation = rotation; }
        }

        void WriteInput(PlayerInput input)
        {
            if (!IsBoundInput(input) || input.RawInput == null) return;
            if (!ControlsActive()) NeutralHands();
            bool isLeft = input == left;
            ControllerInput state = isLeft ? leftState : rightState;
            hands.Fill(input, state, isLeft, ControlsActive() ? movement : Vector2.zero, ControlsActive() && running);
            input.RawInput.SetAndCallEvents(input, state);
        }

        void NeutralHands()
        {
            movement = Vector2.zero; running = false;
            hands.Sample(false, ToggleGrips, HandReach, Quaternion.Euler(pitch, yaw, 0f), HeadPosition(), 0f);
        }

        public void LateUpdate()
        {
            if (!requested || !Owns(player)) return;
            if (!ControlsActive() && captured) ReleaseCapture();
            SampleInput(); ApplyHeadPose();
            if (OwnsInput(left)) hands.ApplyPose(left, true);
            if (OwnsInput(right)) hands.ApplyPose(right, false);
            if (captured) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        }

        public void ReleaseItems()
        {
            if (player == null) return;
            hands.Clear();
            hands.Sample(false, ToggleGrips, HandReach, Quaternion.Euler(pitch, yaw, 0f), HeadPosition(), 0f);
            WriteInput(left); WriteInput(right); hands.ReleaseItems(player);
        }

        public void ResetHands() { hands.ResetHands(); sampleFrame = -1; }

        void SavePose(Transform target)
        {
            if (target == null) return;
            foreach (PoseSnapshot pose in savedPoses) if (pose.Target == target) return;
            savedPoses.Add(new PoseSnapshot(target));
        }

        void Detach()
        {
            ReleaseCapture();
            if (player != null) hands.ReleaseItems(player);
            hands.Clear();
            // Never re-enable a controller that death/loading has disabled during this session.
            if (locomotion != null && !smoothBefore && locomotion.IsEnabled) locomotion.IsEnabled = false;
            foreach (PoseSnapshot pose in savedPoses) pose.Restore();
            savedPoses.Clear();
            if (cursorOwned) { Cursor.lockState = cursorBefore; Cursor.visible = cursorVisibleBefore; }
            cursorOwned = false; player = null; locomotion = null; left = right = null; head = cameraHead = leftTarget = rightTarget = null;
            captured = false; sampleFrame = -1; movement = Vector2.zero; running = false;
        }

        public void Dispose()
        {
            if (disposed) return;
            requested = false;
            Detach(); disposed = true;
            foreach (MethodBase method in patched) harmony.Unpatch(method, HarmonyPatchType.All, harmony.Id);
            patched.Clear();
            if (current == this) current = null;
        }

        static bool InputUpdatePrefix(PlayerInput __instance)
        {
            DesktopControlSession session = current;
            if (session == null || !session.OwnsInput(__instance)) return true;
            session.SampleInput(); session.WriteInput(__instance);
            if (__instance.DPad != null) __instance.DPad.Update();
            return false;
        }

        static bool InputPositionPrefix(PlayerInput __instance)
        {
            DesktopControlSession session = current;
            if (session == null || !session.OwnsInput(__instance)) return true;
            session.SampleInput(); session.hands.ApplyPose(__instance, __instance == session.left);
            return false;
        }

        static bool TrackedPositionPrefix(Transform ___transform)
        {
            DesktopControlSession session = current;
            if (session == null || !session.Owns(session.player)) return true;
            if (___transform == session.head || ___transform == session.cameraHead) { session.ApplyHeadPose(); return false; }
            if (session.OwnsInput(session.left) && ___transform == session.leftTarget) { session.hands.ApplyPose(session.left, true); return false; }
            if (session.OwnsInput(session.right) && ___transform == session.rightTarget) { session.hands.ApplyPose(session.right, false); return false; }
            return true;
        }

        static bool ProvidingPosition(IXrDeviceSdkController device, SmoothLocomotion owner)
        {
            DesktopControlSession session = current;
            return session != null && session.OwnsLocomotion(owner) || device != null && device.IsProvidingPosition;
        }

        static DirectionMode HeadDirection(DirectionMode original, SmoothLocomotion owner)
        {
            DesktopControlSession session = current;
            return session != null && session.OwnsLocomotion(owner) ? DirectionMode.Head : original;
        }

        static RunMode HoldRun(RunMode original, SmoothLocomotion owner)
        {
            DesktopControlSession session = current;
            return session != null && session.OwnsLocomotion(owner) ? RunMode.Hold : original;
        }

        static bool NormalRun(bool original, SmoothLocomotion owner)
        {
            DesktopControlSession session = current;
            return session != null && session.OwnsLocomotion(owner) ? false : original;
        }

        static IEnumerable<CodeInstruction> ProviderTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo original = AccessTools.PropertyGetter(typeof(IXrDeviceSdkController), "IsProvidingPosition");
            MethodInfo replacement = AccessTools.Method(typeof(DesktopControlSession), "ProvidingPosition");
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions) {
                if (instruction.Calls(original)) {
                    CodeInstruction argument = new CodeInstruction(OpCodes.Ldarg_0);
                    argument.labels.AddRange(instruction.labels); instruction.labels.Clear();
                    argument.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                    yield return argument;
                    instruction.opcode = OpCodes.Call; instruction.operand = replacement; replaced++;
                }
                yield return instruction;
            }
            if (replaced != 1) throw new InvalidOperationException("Desktop movement provider check is incompatible with this game build.");
        }

        static IEnumerable<CodeInstruction> DirectionTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            int directions = 0, runs = 0, inversions = 0;
            foreach (CodeInstruction instruction in instructions) {
                yield return instruction;
                MethodInfo method = instruction.operand as MethodInfo;
                if (method == null || method.DeclaringType.FullName != "Alta.Utilities.GameSettings") continue;
                string replacement = null;
                if (method.Name == "get_TouchLocomotionMode" || method.Name == "get_PressLocomotionMode") {
                    replacement = "HeadDirection"; directions++;
                } else if (method.Name == "get_SmoothLocomotionRunMode") {
                    replacement = "HoldRun"; runs++;
                } else if (method.Name == "get_IsInvertingSmoothWalkAndRun") {
                    replacement = "NormalRun"; inversions++;
                }
                if (replacement != null) {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(DesktopControlSession), replacement));
                }
            }
            if (directions != 2 || runs != 1 || inversions != 2)
                throw new InvalidOperationException("Desktop movement settings are incompatible with this game build.");
        }

        static float NormalizePitch(float value) { return Mathf.Clamp(value > 180f ? value - 360f : value, -85f, 85f); }
        static float Safe(float value, float fallback) { return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value; }

        sealed class PoseSnapshot
        {
            public readonly Transform Target;
            readonly Transform parent;
            readonly Vector3 position;
            readonly Quaternion rotation;
            public PoseSnapshot(Transform target) { Target = target; parent = target.parent; position = target.localPosition; rotation = target.localRotation; }
            public void Restore() { if (Target != null && Target.parent == parent) { Target.localPosition = position; Target.localRotation = rotation; } }
        }
    }
}
