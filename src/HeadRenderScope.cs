using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using Alta.Character;

namespace ATTSmoothCamera
{
    // Local avatar overrides last only for the streaming camera's draw. The
    // original LOD and skinning states return before another camera renders.
    internal sealed class HeadRenderScope
    {
        static readonly FieldInfo opaque = typeof(ClothesSettings).GetField("headOpaqueClothes", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo transparent = typeof(ClothesSettings).GetField("headTransparentClothes", BindingFlags.Instance | BindingFlags.NonPublic);
        readonly Dictionary<Renderer, ShadowCastingMode> saved = new Dictionary<Renderer, ShadowCastingMode>();
        readonly Dictionary<SkinnedMeshRenderer, bool> skinning = new Dictionary<SkinnedMeshRenderer, bool>();
        LODGroup lod;
        float lodSize;
        public void Begin(PlayerCharacter character, bool visible) { Begin(character, visible, default(Vector3)); }
        public void Begin(PlayerCharacter character, bool visible, Vector3 cameraPosition)
        {
            End();
            if (character == null || character.Dummy == null) return;
            CharacterDummy dummy = character.Dummy;
            if (visible) {
                KeepAvatarLod(dummy.LodGroup, cameraPosition);
                if (dummy.Renderers != null)
                    foreach (Renderer renderer in dummy.Renderers) KeepSkinning(renderer as SkinnedMeshRenderer);
            }
            ClothesSettings clothes = dummy.Clothes;
            if (clothes == null) return;
            Capture(opaque, clothes, visible);
            Capture(transparent, clothes, visible);
        }
        void KeepAvatarLod(LODGroup group, Vector3 cameraPosition)
        {
            if (group == null) return;
            lod = group;
            lodSize = group.size;
            Vector3 scale = group.transform.lossyScale;
            float largestScale = Math.Max(0.001f, Math.Max(Math.Abs(scale.x), Math.Max(Math.Abs(scale.y), Math.Abs(scale.z))));
            float distance = Vector3.Distance(cameraPosition, group.transform.position);
            float required = Math.Max(1f, distance * 8f) / largestScale;
            if (!Single.IsNaN(required) && !Single.IsInfinity(required))
                group.size = Math.Max(lodSize, required);
        }
        void KeepSkinning(SkinnedMeshRenderer renderer)
        {
            if (renderer == null || skinning.ContainsKey(renderer)) return;
            skinning.Add(renderer, renderer.updateWhenOffscreen);
            renderer.updateWhenOffscreen = true;
        }
        void Capture(FieldInfo field, ClothesSettings clothes, bool visible)
        {
            if (field == null) return;
            SkinnedMeshRenderer[] list = field.GetValue(clothes) as SkinnedMeshRenderer[];
            if (list == null) return;
            foreach (SkinnedMeshRenderer renderer in list) {
                if (renderer == null || saved.ContainsKey(renderer)) continue;
                saved.Add(renderer, renderer.shadowCastingMode);
                if (visible) KeepSkinning(renderer);
                renderer.shadowCastingMode = visible ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
            }
        }
        public void End()
        {
            if (lod != null) lod.size = lodSize;
            lod = null;
            foreach (KeyValuePair<SkinnedMeshRenderer, bool> item in skinning) if (item.Key != null) item.Key.updateWhenOffscreen = item.Value;
            skinning.Clear();
            foreach (KeyValuePair<Renderer, ShadowCastingMode> item in saved) if (item.Key != null) item.Key.shadowCastingMode = item.Value;
            saved.Clear();
        }
    }
}
