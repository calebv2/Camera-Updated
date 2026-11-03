using System;
using System.IO;
using UnityEngine;

namespace ATTSmoothCamera
{
    // The caller chooses the local screenshot directory and owns any avatar
    // render scope through the callbacks. No tracked/source camera is changed.
    internal sealed class StreamPhotoCapture
    {
        bool capturing;

        public string LastStatus { get; private set; }

        public StreamPhotoCapture() { LastStatus = "No photo taken yet."; }

        public bool Capture(Camera source, string directory, int scale,
                            Action<Camera> beforeDraw, Action afterDraw)
        {
            if (capturing) { LastStatus = "A photo is already being captured."; return false; }
            if (source == null) { LastStatus = "Photo unavailable: the streaming camera is not ready."; return false; }
            if (String.IsNullOrEmpty(directory)) { LastStatus = "Photo unavailable: no screenshot folder was supplied."; return false; }

            capturing = true;
            GameObject cameraObject = null;
            RenderTexture target = null;
            Texture2D image = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                int width, height;
                ScreenshotDimensions.Calculate(Screen.width, Screen.height, scale, out width, out height);
                string folder = Path.GetFullPath(directory);
                Directory.CreateDirectory(folder);

                target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                target.name = "ATTStreamPhotoTarget";
                target.antiAliasing = 1;
                target.useMipMap = false;
                target.autoGenerateMips = false;
                if (!target.Create()) throw new InvalidOperationException("The graphics device could not create the photo target.");

                cameraObject = new GameObject("ATTStreamPhotoCamera");
                // AddComponent(Camera) may start enabled. Keeping the object
                // inactive while configuring prevents an automatic screen draw.
                cameraObject.SetActive(false);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.CopyFrom(source);
                camera.enabled = false;
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.targetTexture = target;
                camera.targetDisplay = 0;
                camera.rect = new Rect(0f, 0f, 1f, 1f);
                camera.aspect = (float)width / height;
                if (camera.clearFlags == CameraClearFlags.Depth || camera.clearFlags == CameraClearFlags.Nothing)
                    camera.clearFlags = CameraClearFlags.Skybox;
                camera.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                camera.ResetProjectionMatrix();
                camera.ResetWorldToCameraMatrix();
                camera.ResetCullingMatrix();
                cameraObject.SetActive(true);

                // The caller suppresses its generic camera callbacks while this
                // dedicated render owns its separate avatar scope. Restore that
                // scope before potentially expensive pixel readback/encoding.
                try
                {
                    if (beforeDraw != null) beforeDraw(camera);
                    camera.Render();
                }
                finally
                {
                    if (afterDraw != null) afterDraw();
                }

                RenderTexture.active = target;
                image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                image.Apply(false, false);
                byte[] png = image.EncodeToPNG();
                if (png == null || png.Length == 0) throw new InvalidOperationException("PNG encoding returned no image.");

                string stem = "ATT_Stream_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
                string path = null;
                for (int suffix = 0; suffix < 10000; ++suffix)
                {
                    string candidate = Path.Combine(folder, stem + (suffix == 0 ? "" : "_" + suffix) + ".png");
                    FileStream file;
                    try { file = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
                    catch (IOException)
                    {
                        if (File.Exists(candidate)) continue;
                        throw;
                    }
                    try
                    {
                        using (file) file.Write(png, 0, png.Length);
                        path = candidate;
                    }
                    catch
                    {
                        // Remove only the new, incomplete file this capture owns.
                        try { File.Delete(candidate); } catch { }
                        throw;
                    }
                    break;
                }
                if (path == null) throw new IOException("Could not reserve a unique photo filename.");
                LastStatus = "Photo saved (" + width + " x " + height + "): " + path;
                return true;
            }
            catch (Exception error)
            {
                LastStatus = "Photo failed: " + error.Message;
                return false;
            }
            finally
            {
                // Put the caller's framebuffer back even on allocation, render,
                // callback, encoding, or disk failure. No AudioListener is added.
                RenderTexture.active = previous;
                if (cameraObject != null) UnityEngine.Object.Destroy(cameraObject);
                if (image != null) UnityEngine.Object.Destroy(image);
                if (target != null)
                {
                    target.Release();
                    UnityEngine.Object.Destroy(target);
                }
                capturing = false;
            }
        }

    }
}
