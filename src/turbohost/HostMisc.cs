using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Threading;
using Turbo.Plugins;

namespace SuperD3Hud.TurboHost
{
    /// <summary>A texture handle. The host has no game textures, so it draws nothing.</summary>
    public class HostTexture : ITexture
    {
        private readonly float _width;
        private readonly float _height;
        private float _opacity;

        public HostTexture(float width, float height)
        {
            _width = IsFinite(width) && width > 0f ? width : 1f;
            _height = IsFinite(height) && height > 0f ? height : 1f;
            _opacity = 1f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public float Height { get { return _height; } }
        public float Width { get { return _width; } }

        public float Opacity
        {
            get { return _opacity; }
            set { _opacity = value < 0 ? 0 : (value > 1 ? 1 : value); }
        }

        public void Draw(SharpDX.RectangleF rectangle, float opacity = 1.0f) { }
        public void Draw(float x, float y, float w, float h, float opacityMultiplier = 1.0f) { }
    }

    /// <summary>Texture controller. Every texture is an empty placeholder.</summary>
    public class HostTextureController : ITextureController
    {
        private readonly HostCanvas _canvas;
        private readonly Dictionary<string, ITexture> _textures =
            new Dictionary<string, ITexture>(StringComparer.OrdinalIgnoreCase);
        private readonly object _sync = new object();

        public HostTextureController(HostCanvas canvas)
        {
            _canvas = canvas;
        }

        private ITexture Placeholder(string key)
        {
            if (string.IsNullOrEmpty(key)) key = "empty";
            lock (_sync)
            {
                ITexture texture;
                if (_textures.TryGetValue(key, out texture)) return texture;
                // A stable, non-zero handle is safer for plugins that use the
                // dimensions to lay out a control before drawing it. Rendering
                // remains a no-op because the host has no game texture atlas.
                texture = new HostTexture(1f, 1f);
                _textures[key] = texture;
                return texture;
            }
        }

        public ITexture GetTexture(string name) { return Placeholder("name:" + (name ?? "")); }
        public ITexture GetTexture(uint id) { return Placeholder("id:" + id.ToString()); }
        public ITexture GetTexture(uint textureSno, int frameIndex)
        {
            return Placeholder("sno:" + textureSno.ToString() + ":" + frameIndex.ToString());
        }
        public ITexture GetItemBackgroundTexture(IItem item)
        {
            return Placeholder("item-background:" + (item == null ? "0" : item.AcdId.ToString()));
        }
        public ITexture GetItemTexture(ISnoItem snoItem)
        {
            return Placeholder("item:" + (snoItem == null ? "0" : snoItem.Sno.ToString()));
        }

        public ITexture EmptySocketTexture { get { return Placeholder("empty-socket"); } }
        public ITexture UnidTexture { get { return Placeholder("unid"); } }
        public ITexture KanaiCubeTexture { get { return Placeholder("kanai-cube"); } }
        public ITexture ButtonTextureGray { get { return Placeholder("button-gray"); } }
        public ITexture ButtonTextureBlue { get { return Placeholder("button-blue"); } }
        public ITexture ButtonTextureOrange { get { return Placeholder("button-orange"); } }
        public ITexture BackgroundTextureOrange { get { return Placeholder("background-orange"); } }
        public ITexture BackgroundTextureGreen { get { return Placeholder("background-green"); } }
        public ITexture BackgroundTextureYellow { get { return Placeholder("background-yellow"); } }
        public ITexture BackgroundTextureBlue { get { return Placeholder("background-blue"); } }
        public ITexture Button2TextureGray { get { return Placeholder("button2-gray"); } }
        public ITexture Button2TextureOrange { get { return Placeholder("button2-orange"); } }
        public ITexture Button2TextureBrown { get { return Placeholder("button2-brown"); } }
        public ITexture BuffFrameTexture { get { return Placeholder("buff-frame"); } }
        public ITexture DebuffFrameTexture { get { return Placeholder("debuff-frame"); } }
        public ITexture InventorySlotTexture { get { return Placeholder("inventory-slot"); } }
        public ITexture InventoryLegendaryBackgroundSmall { get { return Placeholder("inventory-legendary-small"); } }
        public ITexture InventoryLegendaryBackgroundLarge { get { return Placeholder("inventory-legendary-large"); } }
        public ITexture InventorySetBackgroundSmall { get { return Placeholder("inventory-set-small"); } }
        public ITexture InventorySetBackgroundLarge { get { return Placeholder("inventory-set-large"); } }
    }

    /// <summary>Stat controller. The host does not profile plugins.</summary>
    public class HostStatController : IStatController
    {
        private readonly PerfCounter _render = new PerfCounter();
        private readonly PerfCounter _monsterHitpoints = new PerfCounter();

        public IPerfCounter RenderPerfCounter { get { return _render; } }
        public IPerfCounter MonsterHitpointDecreasePerfCounter { get { return _monsterHitpoints; } }
    }

    /// <summary>Sound controller. Speech is disabled because the host ships no TTS voice.</summary>
    public class HostSoundController : ISoundController
    {
        private readonly Watch _lastSpeak = new Watch();
        private bool _speakEnabled;
        private VolumeMode _volumeMode = VolumeMode.AutoMasterAndEffects;
        private double _volumeMultiplier = 1.0;
        private int _constantVolume = 100;

        public bool IsSpeakEnabled
        {
            get { return _speakEnabled; }
            set { _speakEnabled = value; }
        }

        public IWatch LastSpeak { get { return _lastSpeak; } }
        public void Speak(string text) { }
        public void StopSpeak() { }

        public bool IsIngameSoundEnabled { get { return false; } }
        public int IngameMasterVolume { get { return 100; } }
        public int IngameEffectsVolume { get { return 100; } }

        public VolumeMode VolumeMode
        {
            get { return _volumeMode; }
            set { _volumeMode = value; }
        }

        public double VolumeMultiplier
        {
            get { return _volumeMultiplier; }
            set { _volumeMultiplier = double.IsNaN(value) || double.IsInfinity(value)
                ? 1.0 : Math.Max(0.0, value); }
        }

        public int ConstantVolume
        {
            get { return _constantVolume; }
            set { _constantVolume = value < 0 ? 0 : (value > 100 ? 100 : value); }
        }

        public SoundPlayer LoadSoundPlayer(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;

            try
            {
                string safeName = Path.GetFileName(fileName);
                if (string.IsNullOrEmpty(safeName) || !string.Equals(safeName, fileName,
                    StringComparison.OrdinalIgnoreCase)) return null;
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sounds", safeName);
                if (!File.Exists(path)) return null;
                return new SoundPlayer(path);
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>Tracker controller. The host keeps no historical statistics.</summary>
    public class HostTrackerController : ITrackerController
    {
        private IStatTracker SafeTracker()
        {
            IStatTracker tracker = HostStubs.Get<IStatTracker>();
            if (tracker != null) return tracker;
            return new StubIStatTracker();
        }

        public IStatTracker Session { get { return SafeTracker(); } }
        public IStatTracker SessionAlwaysRunning { get { return SafeTracker(); } }
        public IStatTracker CurrentAccountTotal { get { return SafeTracker(); } }
        public IStatTracker CurrentAccountLastMonth { get { return SafeTracker(); } }
        public IStatTracker CurrentAccountLastWeek { get { return SafeTracker(); } }
        public IStatTracker CurrentAccountYesterday { get { return SafeTracker(); } }
        public IStatTracker CurrentAccountToday { get { return SafeTracker(); } }
        public IStatTracker CurrentHeroTotal { get { return SafeTracker(); } }
        public IStatTracker CurrentHeroLastMonth { get { return SafeTracker(); } }
        public IStatTracker CurrentHeroLastWeek { get { return SafeTracker(); } }
        public IStatTracker CurrentHeroYesterday { get { return SafeTracker(); } }
        public IStatTracker CurrentHeroToday { get { return SafeTracker(); } }
    }

    /// <summary>Text log controller. Writes under logs\ next to the launcher.</summary>
    public class HostTextLogController : ITextLogController
    {
        private int _exceptionCount;

        public int ExceptionCount { get { return _exceptionCount; } }

        private static string SafeFileName(string fileName)
        {
            string name = string.IsNullOrEmpty(fileName) ? "plugins.log" : Path.GetFileName(fileName);
            return string.IsNullOrEmpty(name) ? "plugins.log" : name;
        }

        public DateTime Log(string fileName, string text, bool appendTimeStamp = true, bool append = true)
        {
            DateTime now = DateTime.Now;

            try
            {
                string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                Directory.CreateDirectory(root);

                string name = SafeFileName(fileName);
                string path = Path.Combine(root, name);

                string line = text ?? "";
                if (appendTimeStamp)
                    line = now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line;

                if (append)
                    File.AppendAllText(path, line + Environment.NewLine);
                else
                    File.WriteAllText(path, line + Environment.NewLine);
            }
            catch
            {
                // Logging must never break a frame.
                Interlocked.Increment(ref _exceptionCount);
            }

            return now;
        }

        public void LogParagonLevel() { }

        public void Delete(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return;
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", SafeFileName(fileName));
                if (File.Exists(path)) File.Delete(path);
            }
            catch { Interlocked.Increment(ref _exceptionCount); }
        }
    }
}
