using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Modding;
using UnityEngine;
using UnityEngine.Audio;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace PlayableVessel
{
    /// <summary>
    /// Plays any of the game's songs on demand, with its own volume and on/off switch.
    ///
    /// Where the songs come from: every level has a "_SceneManager" object whose SceneManager
    /// component holds that level's music cue. The Modding API's scene repacker (the same native tool it
    /// uses for preloads) can pull those objects, and everything they reference, out of all levels into
    /// asset bundles. That's done once in the background ("Build library") and cached in the mod folder;
    /// after that the library loads in moments. Songs the game plays while you explore (boss themes
    /// applied by fights, etc.) are added as you hear them.
    ///
    /// The overlay opens with M (keyboard) or a left stick click (controller).
    /// </summary>
    public class MusicPlayer : MonoBehaviour
    {
        internal static MusicPlayer Instance;

        private class Song
        {
            public string Name;
            public MusicCue Cue;
            public AudioClip Clip;
        }

        private readonly List<Song> _songs = new List<Song>();
        private readonly HashSet<string> _names = new HashSet<string>();
        private readonly List<AssetBundle> _bundles = new List<AssetBundle>();
        private AudioSource _src;
        private Song _playing;
        private int _selected;
        private int _scroll;
        private bool _open;
        private bool _tookControl;
        private string _status = "";
        private volatile bool _building;
        private volatile string _buildProgress = "";
        private List<string> _builtFiles;

        private static string CacheDir => Path.Combine(PlayableVessel.ModDir, "MusicLibrary");

        internal static void Create()
        {
            if (Instance != null) return;
            var go = new GameObject("PlayableVessel Music Player");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<MusicPlayer>();
        }

        private void Awake()
        {
            _src = gameObject.AddComponent<AudioSource>();
            _src.loop = true;
            _src.playOnAwake = false;
            _src.spatialBlend = 0f;
            _src.ignoreListenerPause = true;
            On.AudioManager.ApplyMusicCue += OnApplyMusicCue;
        }

        private void Start()
        {
            LoadCachedLibrary();
            foreach (var cue in Resources.FindObjectsOfTypeAll<MusicCue>()) AddCue(cue);
            if (_songs.Count == 0) _status = "Library not built yet: build it from the mod menu (Music library).";
        }

        private void OnDestroy() => On.AudioManager.ApplyMusicCue -= OnApplyMusicCue;

        // ------------------------------------------------------------ library

        /// <summary>Record every cue the game plays, so boss themes etc. join the list once heard.</summary>
        private void OnApplyMusicCue(On.AudioManager.orig_ApplyMusicCue orig, AudioManager self, MusicCue cue, float delay, float transition, bool snapshot)
        {
            AddCue(cue);
            orig(self, cue, delay, transition, snapshot);
        }

        private void AddCue(MusicCue cue)
        {
            if (cue == null) return;
            var clip = MainClip(cue);
            if (clip == null) return;
            string name = Pretty(cue.name);
            if (!_names.Add(name)) return;
            _songs.Add(new Song { Name = name, Cue = cue, Clip = clip });
            _songs.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }

        private static string Pretty(string n) => n.Replace("_", " ").Trim();

        /// <summary>The cue's main stem (the full song); falls back to the first channel that has a clip.</summary>
        private static AudioClip MainClip(MusicCue cue)
        {
            AudioClip first = null;
            foreach (MusicChannels ch in Enum.GetValues(typeof(MusicChannels)))
            {
                MusicCue.MusicChannelInfo info;
                try { info = cue.GetChannelInfo(ch); } catch { continue; }
                if (info == null || info.Clip == null) continue;
                if (ch == MusicChannels.Main) return info.Clip;
                if (first == null) first = info.Clip;
            }
            return first;
        }

        private void LoadCachedLibrary()
        {
            if (!Directory.Exists(CacheDir)) return;
            int before = _songs.Count;
            foreach (var file in Directory.GetFiles(CacheDir, "*.bundle").OrderBy(f => f))
            {
                try
                {
                    var b = AssetBundle.LoadFromFile(file);
                    if (b == null) continue;
                    _bundles.Add(b);
                    // Load only the music cues (and, through them, their audio). The level objects the cues were
                    // packed with (shade, borders, mixer snapshots) are only loaded if the bundle has no cue entries.
                    var cues = b.LoadAllAssets<MusicCue>();
                    if (cues != null && cues.Length > 0)
                        foreach (var c in cues) AddCue(c);
                    else
                        foreach (var go in b.LoadAllAssets<GameObject>())
                            foreach (var sm in go.GetComponentsInChildren<SceneManager>(true))
                            {
                                AddCue(GetField<MusicCue>(sm, "musicCue"));
                                AddCue(GetField<MusicCue>(sm, "infectedMusicCue"));
                            }
                }
                catch (Exception e)
                {
                    PlayableVessel.Instance.LogWarn($"Music library file {Path.GetFileName(file)}: {e.Message}");
                }
            }
            PlayableVessel.Instance.Log($"Music library: {_songs.Count - before} songs from cache");
        }

        private static T GetField<T>(object o, string name) where T : class =>
            o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(o) as T;

        /// <summary>One-time: repack every level's "_SceneManager" into cached bundles, on a background thread.</summary>
        internal void BuildLibrary()
        {
            if (_building) return;
            var repacker = typeof(Mod).Assembly.GetType("Modding.UnitySceneRepacker");
            var repack = repacker?.GetMethod("Repack", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var modeType = repacker?.GetNestedType("Mode", BindingFlags.Public | BindingFlags.NonPublic);
            if (repack == null || modeType == null)
            {
                _status = "This Modding API version has no scene repacker; only songs you hear are listed.";
                return;
            }
            object assetMode = Enum.Parse(modeType, "AssetBundle");
            string dataPath = Application.dataPath;
            var scenes = new List<string>();
            for (int i = 0; i < USceneManager.sceneCountInBuildSettings; i++)
            {
                string s = Path.GetFileNameWithoutExtension(UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(i));
                if (!string.IsNullOrEmpty(s) && s != "Pre_Menu_Intro" && s != "Menu_Title" && s != "Quit_To_Menu") scenes.Add(s);
            }

            _building = true;
            _status = "Building music library (runs in the background)...";
            Directory.CreateDirectory(CacheDir);
            var thread = new Thread(() =>
            {
                var files = new List<string>();
                const int batch = 40; // small batches: one broken level can't sink the whole library
                for (int start = 0; start < scenes.Count; start += batch)
                {
                    var chunk = scenes.Skip(start).Take(batch).ToList();
                    _buildProgress = $"{Math.Min(start + batch, scenes.Count)}/{scenes.Count} levels";
                    string json = "{" + string.Join(",", chunk.Select(s => $"\"{s}\":[\"_SceneManager\"]").ToArray()) + "}";
                    try
                    {
                        var result = repack.Invoke(null, new[] { $"pv_music_{start / batch:D2}", dataPath, json, assetMode });
                        var bytes = (byte[])result.GetType().GetField("Item1").GetValue(result);
                        if (bytes == null || bytes.Length == 0) continue;
                        string file = Path.Combine(CacheDir, $"music_{start / batch:D2}.bundle");
                        File.WriteAllBytes(file, bytes);
                        files.Add(file);
                    }
                    catch (Exception e)
                    {
                        PlayableVessel.Instance.LogWarn($"Music library batch {start / batch}: {(e.InnerException ?? e).Message}");
                    }
                }
                _builtFiles = files;
                _building = false;
            }) { IsBackground = true, Name = "PlayableVessel music library" };
            thread.Start();
        }

        // ------------------------------------------------------------ playback

        private void Play(Song song)
        {
            if (song?.Clip == null) return;
            _playing = song;
            _src.clip = song.Clip;
            if (song.Clip.loadState == AudioDataLoadState.Unloaded) song.Clip.LoadAudioData();
            _src.Play();
            PlayableVessel.GS.MusicSong = song.Name;
            PlayableVessel.GS.MusicOn = true;
            _status = "Playing: " + song.Name;
        }

        private void Stop()
        {
            _src.Stop();
            _playing = null;
            PlayableVessel.GS.MusicOn = false;
            SetGameMusicMuted(false);
            _status = "Stopped (game music back on)";
        }

        internal void ApplySettings()
        {
            if (!PlayableVessel.GS.MusicOn) { if (_playing != null) Stop(); return; }
            if (_playing == null)
            {
                var s = _songs.FirstOrDefault(x => x.Name == PlayableVessel.GS.MusicSong);
                if (s != null) Play(s);
            }
        }

        private AudioSource[] GameMusicSources()
        {
            var am = GameManager.instance != null ? GameManager.instance.AudioManager : null;
            return am != null ? GetField<AudioSource[]>(am, "musicSources") : null;
        }

        private void SetGameMusicMuted(bool mute)
        {
            var srcs = GameMusicSources();
            if (srcs == null) return;
            foreach (var s in srcs) if (s != null) s.mute = mute;
        }

        private void Update()
        {
            if (_builtFiles != null)
            {
                var files = _builtFiles;
                _builtFiles = null;
                foreach (var b in _bundles) b.Unload(false);
                _bundles.Clear();
                int before = _songs.Count;
                LoadCachedLibrary();
                _status = $"Library built: {_songs.Count} songs ({_songs.Count - before} new)";
            }

            // Our song goes through the game's music mixer group, so the game's music volume still applies.
            var game = GameMusicSources();
            if (game != null && game.Length > 0 && game[0] != null && _src.outputAudioMixerGroup == null)
                _src.outputAudioMixerGroup = game[0].outputAudioMixerGroup;
            _src.volume = PlayableVessel.GS.MusicVolume;
            if (_playing != null) SetGameMusicMuted(true);
            if (PlayableVessel.GS.MusicOn && _playing == null && _songs.Count > 0 && !string.IsNullOrEmpty(PlayableVessel.GS.MusicSong))
                ApplySettings();

            HandleInput();
        }

        // ------------------------------------------------------------ overlay

        private bool TogglePressed()
        {
            bool key = false;
            try { key = Input.GetKeyDown((KeyCode)Enum.Parse(typeof(KeyCode), PlayableVessel.GS.MusicKey, true)); } catch { }
            var dev = InControl.InputManager.ActiveDevice;
            return key || (dev != null && dev.LeftStickButton.WasPressed);
        }

        private void HandleInput()
        {
            var gm = GameManager.instance;
            bool paused = gm != null && gm.isPaused;
            if (TogglePressed() && (_open || !paused)) SetOpen(!_open);
            if (!_open || InputHandler.Instance == null) return;

            // Close on pause, room change or death; otherwise keep the Knight frozen every frame, so a room
            // transition or anything else that hands control back can't leave the list open during play.
            var hc = HeroController.instance;
            if (paused || hc == null || hc.cState.transitioning || hc.cState.dead || hc.cState.hazardDeath) { SetOpen(false); return; }
            if (hc.acceptingInput) { hc.RelinquishControl(); _tookControl = true; }

            var a = InputHandler.Instance.inputActions;
            if (a.down.WasPressed) _selected = Math.Min(_songs.Count - 1, _selected + 1);
            if (a.up.WasPressed) _selected = Math.Max(0, _selected - 1);
            if (a.right.WasPressed) PlayableVessel.GS.MusicVolume = Mathf.Clamp01(PlayableVessel.GS.MusicVolume + 0.05f);
            if (a.left.WasPressed) PlayableVessel.GS.MusicVolume = Mathf.Clamp01(PlayableVessel.GS.MusicVolume - 0.05f);
            if (a.jump.WasPressed && _selected < _songs.Count) Play(_songs[_selected]);
            if (a.attack.WasPressed) { if (_playing != null) Stop(); else if (_selected < _songs.Count) Play(_songs[_selected]); }
            if (a.menuCancel.WasPressed || a.pause.WasPressed) SetOpen(false);
        }

        private void SetOpen(bool open)
        {
            if (open != _open) VesselLog.Write("music", (open ? "list opened " : "list closed ") + VesselLog.Hero());
            _open = open;
            var hc = HeroController.instance;
            if (hc == null) return;
            // While the list is open, the Knight ignores input (the buttons drive the list instead).
            if (open && hc.acceptingInput) { hc.RelinquishControl(); _tookControl = true; }
            else if (!open && _tookControl) { hc.RegainControl(); _tookControl = false; }
        }

        private GUIStyle _box, _row, _sel, _small;

        private void OnGUI()
        {
            if (!_open) return;
            if (_box == null)
            {
                _box = new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.UpperLeft, padding = new RectOffset(14, 14, 10, 10) };
                _row = new GUIStyle(GUI.skin.label) { fontSize = 18 };
                _sel = new GUIStyle(_row) { fontStyle = FontStyle.Bold };
                _sel.normal.textColor = new Color(1f, 0.85f, 0.4f);
                _small = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            }
            float w = 520, h = 560, x = 40, y = 60;
            GUI.Box(new Rect(x, y, w, h), "Music player", _box);
            float cy = y + 40;
            string now = _playing != null ? "Now playing: " + _playing.Name : "Game music";
            GUI.Label(new Rect(x + 14, cy, w - 28, 24), now, _row); cy += 26;
            GUI.Label(new Rect(x + 14, cy, w - 28, 24), $"Volume: {Mathf.RoundToInt(PlayableVessel.GS.MusicVolume * 100)}%", _row); cy += 32;

            const int visible = 12;
            if (_selected < _scroll) _scroll = _selected;
            if (_selected >= _scroll + visible) _scroll = _selected - visible + 1;
            for (int i = _scroll; i < Math.Min(_songs.Count, _scroll + visible); i++)
            {
                bool sel = i == _selected;
                string mark = _songs[i] == _playing ? "♪ " : "   ";
                GUI.Label(new Rect(x + 14, cy, w - 28, 24), (sel ? "> " : "  ") + mark + _songs[i].Name, sel ? _sel : _row);
                cy += 26;
            }
            if (_songs.Count == 0) { GUI.Label(new Rect(x + 14, cy, w - 28, 24), "(no songs yet)", _row); }
            cy = y + h - 96;
            string build = _building ? $"Building... {_buildProgress}" : _status;
            GUI.Label(new Rect(x + 14, cy, w - 28, 40), build, _small); cy += 40;
            GUI.Label(new Rect(x + 14, cy, w - 28, 44),
                "Up/Down: choose   Jump: play   Attack: stop/play   Left/Right: volume\nM / L3 / Back: close   (full library: Options > Mods > Playable Vessel)", _small);
        }
    }
}
