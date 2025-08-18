// SPDX-License-Identifier: MIT
// REPO: "OmniDoom — DOOM WAD autoload (PLAYPAL+COLORMAP) + DOOM Audio"
// Author: Omniscye

using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

// BepInEx shit
using BepInEx;

namespace REPO.CursedGfx
{
    public static class Entry
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Bootstrap() => CursedGraphicsMod.Init();
    }

    [HarmonyPatch]
    public static class CursedGraphicsMod
    {
        private static Harmony _harmony;
        private static bool _patched;

        public static void Init()
        {
            if (_patched) return;
            _patched = true;
            _harmony = new Harmony("repo.cursedgfx.doom.autowad");
            _harmony.PatchAll(typeof(CursedGraphicsMod).Assembly);
            Debug.Log("[CURSED GFX] Harmony patches applied.");
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(RenderTextureMain), methodName: "Awake")]
        private static void RenderTextureMain_Awake_Postfix(RenderTextureMain __instance)
        {
            if (__instance == null) return;
            if (__instance.gameObject.GetComponent<CursedGraphicsController>() == null)
                __instance.gameObject.AddComponent<CursedGraphicsController>();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(RenderTextureMain), methodName: "SetRenderTexture")]
        private static void RenderTextureMain_SetRenderTexture_Postfix(RenderTextureMain __instance)
        {
            try
            {
                if (__instance?.renderTexture != null)
                    __instance.renderTexture.filterMode = FilterMode.Point;
            }
            catch { }
        }
    }

    // === DOOM-ify runtime SFX (patch Sound.Play) ===
    [HarmonyPatch(typeof(Sound), nameof(Sound.Play))]
    public static class Patch_DoomifyAudio
    {
        private static void Postfix(ref AudioSource __result)
        {
            if (__result == null) return;
            var go = __result.gameObject;

            // 3D, no reverb blending from zones.
            __result.spatialBlend = 1f;
            __result.reverbZoneMix = 0f;

            // Doom-ify chain (idempotent if already present):
            if (go.GetComponent<DoomifyFilter>() == null)
            {
                var df = go.AddComponent<DoomifyFilter>();
                df.enabled = DoomAudioManager.GlobalSfxEnabled; // follow global switch
            }

            var lp = go.GetComponent<AudioLowPassFilter>() ?? go.AddComponent<AudioLowPassFilter>();
            lp.cutoffFrequency = 6000f; // Doom SFX top-end is modest
            var hp = go.GetComponent<AudioHighPassFilter>() ?? go.AddComponent<AudioHighPassFilter>();
            hp.cutoffFrequency = 40f;   // trim sub-bass mud
        }
    }

    public sealed class DoomAudioManager : MonoBehaviour
    {
        public static DoomAudioManager Instance { get; private set; }
        public static bool GlobalSfxEnabled = true; // F11 toggles SFX doomify

        private AudioSource _music;
        private AudioClip _clip;     // single track (DoomMusic)
        private bool _active;
        private Coroutine _loader;

        // Always point to BepInEx/plugins/Omniscye-DoomFX/Music (portable, no AppData) we ain't bitches
        private string MusicDir
        {
            get
            {
                try
                {
                    return Path.Combine(Paths.PluginPath, "Omniscye-DoomFX", "Music");
                }
                catch
                {
                    // Fallback for non-BepInEx contexts (still portable next to game)
                    return Path.Combine(Application.dataPath, "..", "plugins", "Omniscye-DoomFX", "Music");
                }
            }
        }

        public static DoomAudioManager Ensure()
        {
            if (Instance != null) return Instance;
            var host = new GameObject("REPO.DoomAudioManager");
            UnityEngine.Object.DontDestroyOnLoad(host);
            Instance = host.AddComponent<DoomAudioManager>();
            return Instance;
        }

        private void Awake()
        {
            _music = gameObject.AddComponent<AudioSource>();
            _music.loop = true;               // single track: just loop it when playing
            _music.playOnAwake = false;
            _music.spatialBlend = 0f;         // 2D music
            _music.reverbZoneMix = 0f;
            _music.volume = 0.6f;
        }

        public void SetActive(bool on)
        {
            _active = on;
            if (on)
            {
                if (_clip == null && _loader == null)
                    _loader = StartCoroutine(LoadClipCoroutine());
                else if (_clip != null && !_music.isPlaying)
                {
                    _music.clip = _clip;
                    _music.Play();
                }
            }
            else
            {
                if (_music.isPlaying) _music.Stop();
            }
        }

        // Single-song toggle: play/pause only
        public void ToggleMusic()
        {
            if (!_active)
            {
                SetActive(true);
                return;
            }

            if (_clip == null)
            {
                if (_loader == null) _loader = StartCoroutine(LoadClipCoroutine());
                return;
            }

            if (_music.isPlaying) _music.Pause();
            else
            {
                if (_music.clip != _clip) _music.clip = _clip;
                // If previously paused, UnPause resumes from position
                if (_music.time > 0f && _music.time < _music.clip.length) _music.UnPause();
                else _music.Play();
            }
        }

        public void Reload()
        {
            if (_loader != null) StopCoroutine(_loader);
            _clip = null;
            if (_music.isPlaying) _music.Stop();
            _loader = StartCoroutine(LoadClipCoroutine());
        }

        private IEnumerator LoadClipCoroutine()
        {
            var baseDir = MusicDir;
            try { Directory.CreateDirectory(baseDir); } catch { }

            // Preferred filename per request
            string chosen = Path.Combine(baseDir, "DoomMusic.mp3");

            // Light fallback if you drop different encodings with same base name
            if (!File.Exists(chosen))
            {
                var ogg = Path.Combine(baseDir, "DoomMusic.ogg");
                var wav = Path.Combine(baseDir, "DoomMusic.wav");
                if (File.Exists(ogg)) chosen = ogg;
                else if (File.Exists(wav)) chosen = wav;
            }

            if (!File.Exists(chosen))
            {
                Debug.Log($"[DOOM AUDIO] No music file found. Place 'DoomMusic.mp3' in: {baseDir}");
                _loader = null;
                yield break;
            }

            var type = GuessAudioTypeFromExtension(chosen);
            var uri = new Uri(chosen).AbsoluteUri; // file://

            using (var req = UnityWebRequestMultimedia.GetAudioClip(uri, type))
            {
#if UNITY_2020_1_OR_NEWER
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
#else
                yield return req.SendWebRequest();
                if (req.isNetworkError || req.isHttpError)
#endif
                {
                    Debug.LogWarning($"[DOOM AUDIO] Failed to load {chosen}: {req.error}");
                    _loader = null;
                    yield break;
                }

                try
                {
                    var clip = DownloadHandlerAudioClip.GetContent(req);
                    if (clip != null) _clip = clip;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[DOOM AUDIO] Exception decoding {chosen}: {e.Message}");
                    _loader = null;
                    yield break;
                }
            }

            if (_active && _clip != null)
            {
                _music.clip = _clip;
                _music.Play();
            }

            _loader = null;
        }

        private static AudioType GuessAudioTypeFromExtension(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".ogg" => AudioType.OGGVORBIS,
                ".wav" => AudioType.WAV,
                ".mp3" => AudioType.MPEG,
                _ => AudioType.UNKNOWN,
            };
        }
    }

    /// <summary>
    /// Lightweight DOOM-style SFX renderer:
    /// - Downmixes to mono
    /// - Resamples to ~11025 Hz using sample-and-hold
    /// - 8-bit style quantization
    /// - Optional soft drive
    /// </summary>
    public class DoomifyFilter : MonoBehaviour
    {
        [Range(8000f, 16000f)] public float targetSampleRate = 11025f;
        [Range(0f, 24f)] public float preGainDb = 6f; // push into soft clip gently
        [Range(0f, 1f)] public float drive = 0.15f;   // 0 = clean, 1 = max

        private int _systemRate;
        private float _holdStep;
        private float _holdCounter;
        private float _last;

        private void Awake()
        {
            _systemRate = AudioSettings.outputSampleRate;
            _holdStep = Mathf.Max(1f, _systemRate / Mathf.Max(1000f, targetSampleRate));
            _holdCounter = 0f;
        }

        private void OnEnable()
        {
            _systemRate = AudioSettings.outputSampleRate;
            _holdStep = Mathf.Max(1f, _systemRate / Mathf.Max(1000f, targetSampleRate));
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!DoomAudioManager.GlobalSfxEnabled || channels <= 0) return;

            float pregain = Mathf.Pow(10f, preGainDb / 20f);
            for (int i = 0; i < data.Length; i += channels)
            {
                // Downmix to mono
                float m = 0f;
                for (int c = 0; c < channels; c++) m += data[i + c];
                m /= channels;

                // Sample-hold to target rate
                _holdCounter += 1f;
                if (_holdCounter >= _holdStep)
                {
                    _holdCounter = 0f;
                    // 8-bit style quantization (signed)
                    float q = Mathf.Clamp(m * pregain, -1f, 1f);
                    // Soft saturation before quantize
                    if (drive > 0f)
                    {
                        // Smooth tanh drive
                        q = (float)System.Math.Tanh(q * (1f + drive * 10f));
                    }
                    // Map [-1,1] -> [0,255] -> back to [-1,1]
                    int q8 = Mathf.Clamp(Mathf.RoundToInt((q * 0.5f + 0.5f) * 255f), 0, 255);
                    _last = (q8 / 255f - 0.5f) * 2f;
                }

                for (int c = 0; c < channels; c++) data[i + c] = _last; // mono to all
            }
        }
    }

    public enum CursedStyle
    {
        DOOM = 0,        // Uses real PLAYPAL+COLORMAP if found; else falls back to grade/posterize cause I can't trust fuck all
        DMG_GREEN = 1,
        DMG_GRAY = 2,
        RGB_332 = 3,
        NESISH = 4,
    }

    public sealed class CursedGraphicsController : MonoBehaviour
    {
        // === Options ===
        public bool ModEnabled = false; // start OFF
        public CursedStyle Style = CursedStyle.DOOM;

        [Range(64, 960)] public int TargetWidth = 320;
        [Range(64, 960)] public int TargetHeight = 200;

        // Doom-ish fallback tuning
        [Range(0, 2)] public int Sharpen = 1;
        public bool Scanlines = true;
        [Range(2, 8)] public int Posterize = 6;
        [Range(0.5f, 1.5f)] public float Gamma = 0.9f;
        [Range(0.5f, 1.5f)] public float Contrast = 1.2f;
        [Range(0f, 1f)] public float Desaturate = 0.25f;
        [Range(-0.25f, 0.25f)] public float WarmShift = 0.06f;

        // DOOM palette lighting controls (when PLAYPAL/COLORMAP present)
        [Range(0.25f, 4f)] public float DoomLightScale = 1.0f;  // ; / ' to tweak
        [Range(-1f, 1f)] public float DoomLightBias = 0.0f;     // , / . to tweak
        public bool DoomLightByRow = true;

        // Game refs
        private RenderTextureMain _rtm;
        private RenderTexture _rt;

        // Overlays
        private RawImage _gameOverlay;
        private RawImage _cursedOverlay;
        private Texture _originalGameTexture;
        private bool _installedOverlay;

        // Work
        private Texture2D _workTex;
        private Color32[] _buffer;
        private Color32[] _outBuffer;
        private RenderTexture _downscaleRT;

        // Palettes / LUTs
        private bool _doomAssetsLoaded;
        private string _wadUsed = "";
        private Color32[] _doomPalette;   // 256 colors
        private byte[,] _colormap;        // [level, index] (use up to first 32 levels)
        private int[] _rgbToIndexLUT;     // 32*32*32 mapping r5:g5:b5 => palette index

        // Legacy helpers
        private static readonly byte[] Bayer4x4 = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
        private static readonly Color32[] DMG_GREEN =
        {
            new Color32(0x0F,0x38,0x0F,0xFF),
            new Color32(0x30,0x62,0x30,0xFF),
            new Color32(0x8B,0xAC,0x0F,0xFF),
            new Color32(0x9B,0xBC,0x0F,0xFF),
        };
        private static readonly Color32[] DMG_GRAY =
        {
            new Color32(0x0D,0x0D,0x0D,0xFF),
            new Color32(0x30,0x30,0x30,0xFF),
            new Color32(0x8A,0x8A,0x8A,0xFF),
            new Color32(0xC4,0xC4,0xC4,0xFF),
        };
        private static readonly Color32[] NESISH =
        {
            new Color32(84,84,84,255),  new Color32(0,30,116,255),  new Color32(8,76,196,255),
            new Color32(48,50,236,255), new Color32(92,64,255,255), new Color32(172,50,50,255),
            new Color32(228,92,16,255), new Color32(248,120,88,255),new Color32(56,160,0,255),
            new Color32(0,168,0,255),   new Color32(0,168,88,255),  new Color32(252,160,68,255),
            new Color32(216,200,0,255), new Color32(232,232,232,255),
        };

        private void Awake() => CursedGraphicsMod.Init();

        private void Start()
        {
            _rtm = RenderTextureMain.instance;
            if (_rtm == null) { enabled = false; return; }

            _gameOverlay = _rtm.overlayRawImage;
            _rt = _rtm.renderTexture;
            if (_rt != null) _rt.filterMode = FilterMode.Point;
            if (_gameOverlay?.texture != null) _gameOverlay.texture.filterMode = FilterMode.Point;

            AllocateWorking(TargetWidth, TargetHeight);
            if (_gameOverlay != null) _originalGameTexture = _gameOverlay.texture;

            TryLoadDoomAssets(); // auto-find WAD in Omniscye-DoomFX folder

            // Ensure music manager exists
            DoomAudioManager.Ensure();

            Debug.Log(_doomAssetsLoaded
                ? $"[CURSED GFX] DOOM palette mode ACTIVE (from \"{_wadUsed}\"). Starts OFF."
                : "[CURSED GFX] DOOM-ish fallback (no WAD with PLAYPAL+COLORMAP found). Starts OFF.");
        }

        // --------- WAD auto-discovery (plugins/Omniscye-DoomFX) ---------

        private void TryLoadDoomAssets()
        {
            byte[] pp = null, cm = null;

            // 1) Look in plugins/Omniscye-DoomFX/
            string baseDir = Path.Combine(Paths.PluginPath, "Omniscye-DoomFX");
            if (TryLoadFromPreferredNames(baseDir, out pp, out cm, out _wadUsed) ||
                TryScanWadsInDir(baseDir, out pp, out cm, out _wadUsed))
            {
                ParseDoomLumps(pp, cm);
                return;
            }

            // 2) Fallback: scan the plugin root for ANY .wad (less strict)
            if (TryScanWadsInDir(Paths.PluginPath, out pp, out cm, out _wadUsed))
            {
                ParseDoomLumps(pp, cm);
                return;
            }

            // 3) Last resort: Resources/DOOM/*. (optional)
            var rPP = Resources.Load<TextAsset>("DOOM/PLAYPAL");
            var rCM = Resources.Load<TextAsset>("DOOM/COLORMAP");
            if (rPP != null && rCM != null)
            {
                _wadUsed = "Resources/DOOM/* (no .wad)";
                ParseDoomLumps(rPP.bytes, rCM.bytes);
                return;
            }

            _doomAssetsLoaded = false;
        }

        private bool TryLoadFromPreferredNames(string dir, out byte[] playpal, out byte[] colormap, out string wadUsed)
        {
            playpal = colormap = null; wadUsed = "";
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;

            string[] candidates = new[]
            {
                "freedm.wad", "freedoom.wad", "freedoom1.wad", "freedoom2.wad",
                "doom.wad", "doom2.wad"
            };

            foreach (var name in candidates)
            {
                string path = Path.Combine(dir, name);
                if (File.Exists(path) && TryLoadFromWad(path, "PLAYPAL", "COLORMAP", out playpal, out colormap))
                {
                    wadUsed = Path.GetFileName(path);
                    return true;
                }
                // case variants
                string up = Path.Combine(dir, name.ToUpperInvariant());
                if (File.Exists(up) && TryLoadFromWad(up, "PLAYPAL", "COLORMAP", out playpal, out colormap))
                {
                    wadUsed = Path.GetFileName(up);
                    return true;
                }
            }
            return false;
        }

        private bool TryScanWadsInDir(string dir, out byte[] playpal, out byte[] colormap, out string wadUsed)
        {
            playpal = colormap = null; wadUsed = "";
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;

            string[] wadFiles = Directory.GetFiles(dir, "*.wad", SearchOption.TopDirectoryOnly);
            if (wadFiles.Length == 0)
                wadFiles = Directory.GetFiles(dir, "*.WAD", SearchOption.TopDirectoryOnly);

            foreach (var path in wadFiles)
            {
                if (TryLoadFromWad(path, "PLAYPAL", "COLORMAP", out playpal, out colormap))
                {
                    wadUsed = Path.GetFileName(path);
                    return true;
                }
            }
            return false;
        }

        private bool TryLoadFromWad(string wadPath, string lumpA, string lumpB, out byte[] a, out byte[] b)
        {
            a = b = null;
            try
            {
                using (var fs = new FileStream(wadPath, FileMode.Open, FileAccess.Read))
                using (var br = new BinaryReader(fs))
                {
                    // Header
                    var ident = new string(br.ReadChars(4)); // "IWAD"/"PWAD"
                    int numLumps = br.ReadInt32();
                    int dirOfs = br.ReadInt32();
                    if (numLumps <= 0 || dirOfs <= 0) return false;

                    // Directory
                    fs.Position = dirOfs;
                    long aOfs = 0, bOfs = 0; int aLen = 0, bLen = 0;
                    for (int i = 0; i < numLumps; i++)
                    {
                        int filepos = br.ReadInt32();
                        int size = br.ReadInt32();
                        string name = new string(br.ReadChars(8)).TrimEnd('\0');

                        if (name.Equals(lumpA, StringComparison.OrdinalIgnoreCase)) { aOfs = filepos; aLen = size; }
                        if (name.Equals(lumpB, StringComparison.OrdinalIgnoreCase)) { bOfs = filepos; bLen = size; }
                    }
                    if (aLen > 0) { fs.Position = aOfs; a = br.ReadBytes(aLen); }
                    if (bLen > 0) { fs.Position = bOfs; b = br.ReadBytes(bLen); }
                    return a != null && b != null;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CURSED GFX] WAD read failed: " + e.Message);
                return false;
            }
        }

        private void ParseDoomLumps(byte[] pp, byte[] cm)
        {
            try
            {
                if (pp == null || cm == null) { _doomAssetsLoaded = false; return; }

                // PLAYPAL: 14*256*3 bytes (we use palette 0)
                if (pp.Length < 14 * 256 * 3) { _doomAssetsLoaded = false; return; }
                _doomPalette = new Color32[256];
                int baseOff = 0; // palette 0
                for (int i = 0; i < 256; i++)
                {
                    int o = baseOff + i * 3;
                    _doomPalette[i] = new Color32(pp[o], pp[o + 1], pp[o + 2], 255);
                }

                // COLORMAP: usually 34 * 256 bytes; we’ll keep all levels but use 32 by default I think..
                int levels = cm.Length / 256;
                int useLevels = Mathf.Clamp(levels, 1, 34);
                _colormap = new byte[useLevels, 256];
                for (int l = 0; l < useLevels; l++)
                    for (int i = 0; i < 256; i++)
                        _colormap[l, i] = cm[l * 256 + i];

                BuildRGBToIndexLUT();
                _doomAssetsLoaded = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CURSED GFX] Failed to parse DOOM lumps: " + e.Message);
                _doomAssetsLoaded = false;
            }
        }

        private void BuildRGBToIndexLUT()
        {
            _rgbToIndexLUT = new int[32 * 32 * 32];
            for (int r5 = 0; r5 < 32; r5++)
                for (int g5 = 0; g5 < 32; g5++)
                    for (int b5 = 0; b5 < 32; b5++)
                    {
                        int r = (r5 << 3) | (r5 >> 2);
                        int g = (g5 << 3) | (g5 >> 2);
                        int b = (b5 << 3) | (b5 >> 2);
                        int best = 0, bestD = int.MaxValue;
                        for (int p = 0; p < 256; p++)
                        {
                            var pc = _doomPalette[p];
                            int dr = r - pc.r; dr *= dr;
                            int dg = g - pc.g; dg *= dg;
                            int db = b - pc.b; db *= db;
                            int d = dr + dg + db;
                            if (d < bestD) { bestD = d; best = p; }
                        }
                        _rgbToIndexLUT[(r5 << 10) | (g5 << 5) | b5] = best;
                    }
        }

        // ----------------- Main loop -----------------

        private void AllocateWorking(int w, int h)
        {
            if (_workTex != null) UnityEngine.Object.Destroy(_workTex);
            _workTex = new Texture2D(w, h, TextureFormat.RGBA32, false, false) { filterMode = FilterMode.Point };
            _buffer = new Color32[w * h];
            _outBuffer = new Color32[w * h];
            EnsureDownscaleRT(w, h);
            if (_cursedOverlay != null) _cursedOverlay.texture = _workTex;
        }

        private void EnsureDownscaleRT(int w, int h)
        {
            if (_downscaleRT != null && (_downscaleRT.width != w || _downscaleRT.height != h))
            {
                _downscaleRT.Release(); UnityEngine.Object.Destroy(_downscaleRT); _downscaleRT = null;
            }
            if (_downscaleRT == null)
            {
                _downscaleRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
                { filterMode = FilterMode.Point, useMipMap = false, autoGenerateMips = false };
                _downscaleRT.Create();
            }
        }

        private void InstallOverlay()
        {
            if (_installedOverlay || _gameOverlay == null) return;
            var go = new GameObject("CURSED Output", typeof(RectTransform), typeof(RawImage));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(_gameOverlay.transform.parent, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            _cursedOverlay = go.GetComponent<RawImage>();
            _cursedOverlay.raycastTarget = false;
            _cursedOverlay.material = _gameOverlay.material;
            _cursedOverlay.texture = _workTex;
            go.transform.SetSiblingIndex(_gameOverlay.transform.GetSiblingIndex());
            _gameOverlay.enabled = false;
            _installedOverlay = true;
        }

        private void RestoreOverlay()
        {
            if (_gameOverlay != null)
            {
                _gameOverlay.enabled = true;
                if (_originalGameTexture != null) _gameOverlay.texture = _originalGameTexture;
            }
            if (_cursedOverlay != null) UnityEngine.Object.Destroy(_cursedOverlay.gameObject);
            _cursedOverlay = null;
            _installedOverlay = false;
        }

        private void Update() => HandleHotkeys();

        private void LateUpdate()
        {
            if (!ModEnabled) return;
            if (_rtm?.renderTexture == null || _workTex == null) return;
            if (!_installedOverlay && _gameOverlay != null) InstallOverlay();

            int w = TargetWidth, h = TargetHeight;
            if (_workTex.width != w || _workTex.height != h) AllocateWorking(w, h);

            _rt = _rtm.renderTexture;
            _rt.filterMode = FilterMode.Point;

            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(_rt, _downscaleRT);
                RenderTexture.active = _downscaleRT;
                _workTex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                _workTex.Apply(false, false);
            }
            finally { RenderTexture.active = prev; }

            var src = _workTex.GetPixels32();
            EnsureBuffers(src.Length);

            if (Style == CursedStyle.DOOM && _doomAssetsLoaded)
            {
                ProcessDoomPalette(src, _outBuffer, w, h);
            }
            else
            {
                Array.Copy(src, _buffer, src.Length);
                ProcessDoomish(_buffer, _outBuffer, w, h);
            }

            _workTex.SetPixels32(_outBuffer);
            _workTex.Apply(false, false);
            if (_cursedOverlay != null && _cursedOverlay.texture != _workTex)
                _cursedOverlay.texture = _workTex;
        }

        private void EnsureBuffers(int n)
        {
            if (_buffer == null || _buffer.Length != n) _buffer = new Color32[n];
            if (_outBuffer == null || _outBuffer.Length != n) _outBuffer = new Color32[n];
        }

        // --- True DOOM palette path ---

        private void ProcessDoomPalette(Color32[] src, Color32[] dst, int w, int h)
        {
            int maxLevels = Math.Min(_colormap.GetLength(0), 32);
            float bias = DoomLightBias;
            float scale = DoomLightScale;

            for (int y = 0; y < h; y++)
            {
                float rowNorm = (float)y / (h - 1);
                float lightF = DoomLightByRow ? (1f - rowNorm) : 1f;

                float L = Mathf.Clamp01(lightF * scale + bias);
                int light = Mathf.Clamp(Mathf.RoundToInt((1f - L) * (maxLevels - 1)), 0, maxLevels - 1);

                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    var c = src[row + x];
                    int key = ((c.r >> 3) << 10) | ((c.g >> 3) << 5) | (c.b >> 3);
                    int baseIdx = _rgbToIndexLUT[key];
                    int palIdx = _colormap[light, baseIdx];
                    dst[row + x] = _doomPalette[palIdx];
                }
            }

            if (Scanlines) DarkenEveryOtherRow(dst, w, h, 0.75f);
            if (Sharpen > 0) ApplySharpen(dst, w, h, Sharpen);
        }

        private static void DarkenEveryOtherRow(Color32[] px, int w, int h, float factor)
        {
            for (int y = 0; y < h; y += 2)
            {
                int yo = y * w;
                for (int x = 0; x < w; x++)
                {
                    int i = yo + x;
                    var c = px[i];
                    c.r = (byte)(c.r * factor);
                    c.g = (byte)(c.g * factor);
                    c.b = (byte)(c.b * factor);
                    px[i] = c;
                }
            }
        }

        // --- Doom-ish fallback ---

        private void ProcessDoomish(Color32[] src, Color32[] dst, int w, int h)
        {
            for (int i = 0; i < src.Length; i++)
            {
                var c = src[i];
                float r = c.r / 255f, g = c.g / 255f, b = c.b / 255f;

                float invG = 1f / Mathf.Max(0.0001f, Gamma);
                r = Mathf.Pow(r, invG); g = Mathf.Pow(g, invG); b = Mathf.Pow(b, invG);

                const float mid = 0.5f;
                r = (r - mid) * Contrast + mid;
                g = (g - mid) * Contrast + mid;
                b = (b - mid) * Contrast + mid;

                float luma = r * 0.299f + g * 0.587f + b * 0.114f;
                r = Mathf.Lerp(r, luma, Desaturate);
                g = Mathf.Lerp(g, luma, Desaturate);
                b = Mathf.Lerp(b, luma, Desaturate);

                r = Mathf.Clamp01(r + WarmShift * 0.9f);
                g = Mathf.Clamp01(g + WarmShift * 0.3f);
                b = Mathf.Clamp01(b - WarmShift * 0.6f);

                src[i] = new Color32(
                    (byte)Mathf.RoundToInt(r * 255f),
                    (byte)Mathf.RoundToInt(g * 255f),
                    (byte)Mathf.RoundToInt(b * 255f),
                    255);
            }

            int lev = Mathf.Clamp(Posterize, 2, 8);
            float step = 255f / (lev - 1);
            for (int i = 0; i < src.Length; i++)
            {
                var c = src[i];
                byte pr = (byte)(Mathf.Round(c.r / step) * step);
                byte pg = (byte)(Mathf.Round(c.g / step) * step);
                byte pb = (byte)(Mathf.Round(c.b / step) * step);
                dst[i] = new Color32(pr, pg, pb, 255);
            }

            if (Scanlines) DarkenEveryOtherRow(dst, w, h, 0.75f);
            if (Sharpen > 0) ApplySharpen(dst, w, h, Sharpen);
        }

        private void ApplySharpen(Color32[] px, int w, int h, int strength)
        {
            var temp = _buffer;
            Array.Copy(px, temp, px.Length);
            for (int pass = 0; pass < strength; pass++)
            {
                for (int y = 1; y < h - 1; y++)
                {
                    int yo = y * w;
                    for (int x = 1; x < w - 1; x++)
                    {
                        int i = yo + x;
                        Color32 c = temp[i];
                        int r = c.r * 5, g = c.g * 5, b = c.b * 5;
                        Color32 l = temp[i - 1], rgt = temp[i + 1], up = temp[i - w], dn = temp[i + w];
                        r -= l.r + rgt.r + up.r + dn.r;
                        g -= l.g + rgt.g + up.g + dn.g;
                        b -= l.b + rgt.b + up.b + dn.b;
                        px[i] = new Color32((byte)Mathf.Clamp(r, 0, 255), (byte)Mathf.Clamp(g, 0, 255), (byte)Mathf.Clamp(b, 0, 255), 255);
                    }
                }
                Array.Copy(px, temp, px.Length);
            }
        }

        // --- Input & GUI ---

        private void HandleHotkeys()
        {
            if (Input.GetKeyDown(KeyCode.F8))
            {
                ModEnabled = !ModEnabled;
                if (ModEnabled) InstallOverlay(); else RestoreOverlay();

                // Music follows graphics master switch
                DoomAudioManager.Ensure().SetActive(ModEnabled);
            }
            if (!ModEnabled) return;

            if (Input.GetKeyDown(KeyCode.F9))
            {
                int next = ((int)Style + 1) % Enum.GetNames(typeof(CursedStyle)).Length;
                Style = (CursedStyle)next;
            }

            if (Input.GetKeyDown(KeyCode.F7)) Scanlines = !Scanlines;

            if (Input.GetKeyDown(KeyCode.LeftBracket)) Sharpen = Mathf.Clamp(Sharpen - 1, 0, 2);
            if (Input.GetKeyDown(KeyCode.RightBracket)) Sharpen = Mathf.Clamp(Sharpen + 1, 0, 2);

            if (Input.GetKeyDown(KeyCode.Minus)) Posterize = Mathf.Clamp(Posterize - 1, 2, 8);
            if (Input.GetKeyDown(KeyCode.Equals)) Posterize = Mathf.Clamp(Posterize + 1, 2, 8);

            // DOOM palette light tweaks
            if (Input.GetKeyDown(KeyCode.Semicolon)) DoomLightScale = Mathf.Clamp(DoomLightScale * 0.9f, 0.25f, 4f);
            if (Input.GetKeyDown(KeyCode.Quote)) DoomLightScale = Mathf.Clamp(DoomLightScale * 1.1f, 0.25f, 4f);
            if (Input.GetKeyDown(KeyCode.Comma)) DoomLightBias = Mathf.Clamp(DoomLightBias - 0.05f, -1f, 1f);
            if (Input.GetKeyDown(KeyCode.Period)) DoomLightBias = Mathf.Clamp(DoomLightBias + 0.05f, -1f, 1f);

            if (Input.GetKeyDown(KeyCode.PageUp))
            {
                TargetWidth = Mathf.Clamp(TargetWidth + 16, 64, 960);
                TargetHeight = Mathf.Clamp(TargetHeight + 10, 64, 960);
                AllocateWorking(TargetWidth, TargetHeight);
            }
            else if (Input.GetKeyDown(KeyCode.PageDown))
            {
                TargetWidth = Mathf.Clamp(TargetWidth - 16, 64, 960);
                TargetHeight = Mathf.Clamp(TargetHeight - 10, 64, 960);
                AllocateWorking(TargetWidth, TargetHeight);
            }

            // Audio controls (single-song)
            if (Input.GetKeyDown(KeyCode.F10)) DoomAudioManager.Ensure().ToggleMusic(); // on/off
            if (Input.GetKeyDown(KeyCode.F12)) DoomAudioManager.Ensure().Reload();
            if (Input.GetKeyDown(KeyCode.F11)) DoomAudioManager.GlobalSfxEnabled = !DoomAudioManager.GlobalSfxEnabled;
        }

        private void OnGUI()
        {
            string mode = Style.ToString();
            string hdr = (_doomAssetsLoaded && Style == CursedStyle.DOOM)
                ? $"OmniDoom (PLAYPAL from \"{_wadUsed}\")"
                : "OmniDoom";

            string txt = ModEnabled
                ? $"{hdr} — {mode} — {TargetWidth}x{TargetHeight}\n" +
                  "[F8 toggle | F9 mode | F7 scanlines | [ ] sharpen | - = posterize | PgUp/PgDn res]\n" +
                  (Style == CursedStyle.DOOM
                     ? $"LightScale:{DoomLightScale:0.00} (;/')  LightBias:{DoomLightBias:+0.00;-0.00;0} (,/.)  RowLight:{(DoomLightByRow ? "ON" : "OFF")}\n"
                     : "") +
                  "Audio: F10 music on/off | F11 SFX Doomify | F12 reload music"
                : $"{hdr} — DISABLED (F8 to enable)";

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = Color.black }
            };

            Rect r = new Rect(10, 10, Screen.width, 80);
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), txt, style);

            style.normal.textColor = Color.green;
            GUI.Label(r, txt, style);
        }
    }
}