using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace ATTSmoothCamera
{
    [Serializable]
    public sealed class CameraPreset
    {
        public string Name = "Camera preset";
        public int Mode;
        public float Distance = 2.8f, Height = 0.15f, Shoulder = 0.4f, Fov = 75f;
        public float PositionHalfLife = 0.08f, RotationHalfLife = 0.12f, OrbitSpeed = 12f;
        public bool HorizonLock = true, SmartFraming = true, AvoidWalls = true;
    }

    [Serializable]
    public sealed class CameraPoint
    {
        public float Time, X, Y, Z, Qx, Qy, Qz;
        public float Qw = 1f, Fov = 75f;
    }

    [Serializable]
    public sealed class CameraPath
    {
        public string Name = "Camera path";
        public List<CameraPoint> Points = new List<CameraPoint>();
    }

    [Serializable, XmlRoot("ATTSmoothCameraLibrary")]
    public sealed class CameraLibraryData
    {
        public int Version = 1;
        public List<CameraPreset> Presets = new List<CameraPreset>();
        public List<CameraPath> Paths = new List<CameraPath>();
    }

    // Contains only local camera settings and positions. No server credentials,
    // player identifiers or game/network state are written to this library.
    public sealed class CameraLibrary
    {
        public const int MaxPresets = 32, MaxPaths = 16, MaxPoints = 2048;
        const long MaxFileBytes = 16 * 1024 * 1024;
        static readonly XmlSerializer Serializer = new XmlSerializer(typeof(CameraLibraryData));
        readonly object gate = new object();
        readonly string directory, file;
        bool needsCorruptBackup;
        public List<CameraPreset> Presets = new List<CameraPreset>();
        public List<CameraPath> Paths = new List<CameraPath>();
        public string Status { get; private set; }

        CameraLibrary(string directory)
        {
            if (String.IsNullOrEmpty(directory)) throw new ArgumentException("Choose a camera library directory.", "directory");
            this.directory = Path.GetFullPath(directory);
            file = Path.Combine(this.directory, "camera-library.xml");
            Status = "Camera library ready.";
        }

        public static CameraLibrary Load(string directory)
        {
            CameraLibrary library = new CameraLibrary(directory);
            if (!File.Exists(library.file)) return library;
            try {
                if (new FileInfo(library.file).Length > MaxFileBytes) throw new InvalidDataException("The library is larger than 16 MB.");
                XmlReaderSettings settings = new XmlReaderSettings();
#pragma warning disable 0618
                settings.ProhibitDtd = true;
#pragma warning restore 0618
                settings.XmlResolver = null;
                CameraLibraryData data;
                using (FileStream input = File.OpenRead(library.file))
                using (XmlReader reader = XmlReader.Create(input, settings))
                    lock (Serializer) data = (CameraLibraryData)Serializer.Deserialize(reader);
                if (data == null || data.Version != 1) throw new InvalidDataException("Unsupported camera library version.");
                library.Import(data);
                library.Status = "Camera library loaded.";
            } catch (Exception error) {
                if (!(error is IOException) && !(error is UnauthorizedAccessException) &&
                    !(error is InvalidOperationException) && !(error is XmlException) &&
                    !(error is ArgumentException)) throw;
                library.Presets.Clear();
                library.Paths.Clear();
                library.needsCorruptBackup = true;
                library.Status = "Could not load the camera library. Its original file will be backed up before saving a new library.";
            }
            return library;
        }

        void Import(CameraLibraryData data)
        {
            if (data.Presets != null)
                foreach (CameraPreset preset in data.Presets) {
                    if (preset == null) continue;
                    UpsertPreset(preset);
                }
            if (data.Paths != null)
                foreach (CameraPath path in data.Paths) {
                    if (path == null) continue;
                    UpsertPath(path);
                }
        }

        public void UpsertPreset(CameraPreset preset)
        {
            CameraPreset clean = CleanPreset(preset);
            lock (gate) {
                if (Presets == null) Presets = new List<CameraPreset>();
                int index = FindPreset(clean.Name);
                if (index >= 0) Presets[index] = clean;
                else {
                    if (Presets.Count >= MaxPresets) throw new InvalidOperationException("You can save up to 32 camera presets.");
                    Presets.Add(clean);
                }
            }
        }
        public void UpsertPath(CameraPath path)
        {
            CameraPath clean = CleanPath(path);
            lock (gate) {
                if (Paths == null) Paths = new List<CameraPath>();
                int index = FindPath(clean.Name);
                if (index >= 0) Paths[index] = clean;
                else {
                    if (Paths.Count >= MaxPaths) throw new InvalidOperationException("You can save up to 16 camera paths.");
                    Paths.Add(clean);
                }
            }
        }
        int FindPreset(string name)
        {
            for (int i = 0; i < Presets.Count; i++) if (Presets[i] != null && String.Equals(Presets[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }
        int FindPath(string name)
        {
            for (int i = 0; i < Paths.Count; i++) if (Paths[i] != null && String.Equals(Paths[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        public void Save()
        {
            lock (gate) {
                // Validate a detached snapshot, including callers' direct list edits,
                // before touching the prior file. Upserts never retain caller-owned DTOs.
                CameraLibrary clean = new CameraLibrary(directory);
                CameraLibraryData raw = new CameraLibraryData();
                raw.Presets = Presets;
                raw.Paths = Paths;
                clean.Import(raw);
                CameraLibraryData data = new CameraLibraryData();
                data.Presets = clean.Presets;
                data.Paths = clean.Paths;
                Directory.CreateDirectory(directory);
                string temporary = Path.Combine(directory, "camera-library." + Guid.NewGuid().ToString("N") + ".tmp");
                try {
                    using (FileStream output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                        lock (Serializer) Serializer.Serialize(output, data);
                        output.Flush();
                    }
                    if (needsCorruptBackup && File.Exists(file)) {
                        string backup = Path.Combine(directory, "camera-library.corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".xml");
                        File.Copy(file, backup, false);
                    }
                    if (File.Exists(file)) File.Replace(temporary, file, Path.Combine(directory, "camera-library.previous.xml"));
                    else File.Move(temporary, file);
                    Presets = clean.Presets;
                    Paths = clean.Paths;
                    needsCorruptBackup = false;
                    Status = "Camera library saved.";
                } finally {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }
        }

        static CameraPreset CleanPreset(CameraPreset p)
        {
            if (p == null) throw new ArgumentNullException("preset");
            return new CameraPreset {
                Name = CleanName(p.Name, "Camera preset"),
                Mode = p.Mode >= 0 && p.Mode <= 4 ? p.Mode : 0,
                Distance = Number(p.Distance, 0.7f, 12f, 2.8f),
                Height = Number(p.Height, -0.4f, 3f, 0.15f),
                Shoulder = Number(p.Shoulder, -3f, 3f, 0.4f),
                Fov = Number(p.Fov, 35f, 110f, 75f),
                PositionHalfLife = Number(p.PositionHalfLife, 0.01f, 2f, 0.08f),
                RotationHalfLife = Number(p.RotationHalfLife, 0.01f, 2f, 0.12f),
                OrbitSpeed = Number(p.OrbitSpeed, -90f, 90f, 12f),
                HorizonLock = p.HorizonLock, SmartFraming = p.SmartFraming, AvoidWalls = p.AvoidWalls
            };
        }
        static CameraPath CleanPath(CameraPath path)
        {
            if (path == null) throw new ArgumentNullException("path");
            if (path.Points == null || path.Points.Count < 2) throw new ArgumentException("A camera path needs at least two points.");
            if (path.Points.Count > MaxPoints) throw new ArgumentException("A camera path can contain up to 2048 points.");
            CameraPath clean = new CameraPath();
            clean.Name = CleanName(path.Name, "Camera path");
            float first = 0f, previous = -1f;
            for (int i = 0; i < path.Points.Count; i++) {
                CameraPoint p = path.Points[i];
                if (p == null || !Finite(p.Time) || p.Time < 0f || (i > 0 && p.Time <= previous))
                    throw new ArgumentException("Camera path times must be finite, nonnegative and strictly increasing.");
                if (!Finite(p.X) || !Finite(p.Y) || !Finite(p.Z) || !Finite(p.Qx) || !Finite(p.Qy) || !Finite(p.Qz) || !Finite(p.Qw))
                    throw new ArgumentException("Camera path positions and rotations must be finite.");
                double norm = Math.Sqrt((double)p.Qx * p.Qx + (double)p.Qy * p.Qy + (double)p.Qz * p.Qz + (double)p.Qw * p.Qw);
                if (norm < 0.000001 || Double.IsNaN(norm) || Double.IsInfinity(norm))
                    throw new ArgumentException("Camera path rotations must contain a valid quaternion.");
                if (i == 0) first = p.Time;
                previous = p.Time;
                clean.Points.Add(new CameraPoint {
                    Time = p.Time - first, X = p.X, Y = p.Y, Z = p.Z,
                    Qx = (float)(p.Qx / norm), Qy = (float)(p.Qy / norm), Qz = (float)(p.Qz / norm), Qw = (float)(p.Qw / norm),
                    Fov = Number(p.Fov, 35f, 110f, 75f)
                });
            }
            return clean;
        }
        static string CleanName(string name, string fallback)
        {
            if (name == null) return fallback;
            StringBuilder result = new StringBuilder();
            foreach (char c in name.Trim()) {
                if (Char.IsControl(c) || Char.IsSurrogate(c)) continue;
                result.Append(c);
            }
            string clean = result.ToString().Trim();
            if (clean.Length > 48) clean = clean.Substring(0, 48).TrimEnd();
            return clean.Length == 0 ? fallback : clean;
        }
        static bool Finite(float value) { return !Single.IsNaN(value) && !Single.IsInfinity(value); }
        static float Number(float value, float min, float max, float fallback) { return Finite(value) ? Math.Max(min, Math.Min(max, value)) : fallback; }
    }
}
