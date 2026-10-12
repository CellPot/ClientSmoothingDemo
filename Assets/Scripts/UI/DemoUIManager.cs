using System.Collections.Generic;
using Mirror;
using Network;
using Techniques;
using UnityEngine;

namespace UI
{
    /// <summary>
    /// Renders the demo overlay UI:
    /// - Left panel: network condition sliders, presets, benchmark
    /// - Right panel: per-technique error metrics
    /// - Top: pause / reset / solo controls
    /// </summary>
    public class DemoUIManager : MonoBehaviour
    {
        [Header("References")] public NetworkSimulator networkSim;
        [Tooltip("Mirror scene: used to detect Mirror mode. Conditions are applied by the server via ServerEntity. Auto-found if empty.")]
        public LatencySimulation mirrorLatency;
        public MirrorNetworkTransport mirrorTransport;
        public List<BaseClientEntity> clients;
        public ServerEntity server;

        // UI state
        private bool _paused;
        private HashSet<int> _hidden = new HashSet<int>();
        private Rect _leftPanel = new Rect(10, 60, 260, 580);
        private Rect _rightPanel;
        private Vector2 _scroll;
        private Vector2 _leftScroll;
        private Vector2 _resultsScroll;
        private bool _showResults;
        private bool _showTuner;
        private int _tunerIndex;
        private Vector2 _tunerScroll;
        private readonly ParameterTuner _tuner = new ParameterTuner();

        // Score weights used by the results window (the CSV always uses the defaults).
        private float _dvWeight = BenchmarkRunner.DefaultDvWeight;
        private float _delayWeight = BenchmarkRunner.DefaultDelayWeight;

        private NetConditions _cond;
        private bool _condInit;
        private BenchmarkRunner _bench;

        private GUIStyle _titleStyle;
        private GUIStyle _metricStyle;
        private GUIStyle _headerStyle;
        private bool _stylesInit;

        // Simulator scene: the Server is a scene object with a NetworkIdentity (ServerEntity is a NetworkBehaviour).
        // Mirror's scene post-processing force-disables such objects until a NetworkServer spawns them, and it can
        // do so after our Awake. There is no network here, so keep switching it on for the first frames.
        private int _simGuardFrames = 120;

        void Awake() => EnsureSimulatorServerActive();

        void EnsureSimulatorServerActive()
        {
            if (networkSim == null) return;
            foreach (var entity in FindObjectsByType<ServerEntity>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!entity.gameObject.activeSelf)
                    entity.gameObject.SetActive(true);
        }

        void Start()
        {
            _rightPanel = new Rect(Screen.width - 280, 60, 270, clients.Count * 100 + 30);
            EnsureSimulatorServerActive();
            if (networkSim == null && mirrorLatency == null)
                mirrorLatency = FindFirstObjectByType<LatencySimulation>();
            if (mirrorTransport == null)
                mirrorTransport = FindFirstObjectByType<MirrorNetworkTransport>();
            // If the ServerEntity is prefab-spawned by DemoNetworkManager, pick it up here
            if (server == null)
                server = ServerEntity.Instance;

            _bench = gameObject.AddComponent<BenchmarkRunner>();
            _bench.Init(this);
        }

        void Update()
        {
            // Late assign in case server spawned after this component started
            if (server == null && ServerEntity.Instance != null)
                server = ServerEntity.Instance;

            if (_simGuardFrames > 0)
            {
                _simGuardFrames--;
                EnsureSimulatorServerActive();
            }

            InitConditions();

            _rightPanel = new Rect(Screen.width - 280, 60, 270, clients.Count * 100 + 40);

            // Apply solo visibility
            for (int i = 0; i < clients.Count; i++)
                clients[i].gameObject.SetActive(!_hidden.Contains(i));

            // Keyboard shortcut: Space = pause, R = reset metrics
            if (Input.GetKeyDown(KeyCode.Space)) TogglePause();
            if (Input.GetKeyDown(KeyCode.R)) ResetAllMetrics();
        }

        // Seed the UI state from whatever the scene/server currently uses.
        void InitConditions()
        {
            if (_condInit) return;
            if (networkSim)
            {
                _cond = new NetConditions
                {
                    latencyMs = networkSim.baseLatencyMs,
                    jitterMs = networkSim.jitterMs,
                    lossPct = networkSim.packetLossRate * 100f,
                    sendRateHz = server ? server.sendRateHz : networkSim.sendRateHz
                };
                _condInit = true;
            }
            else if (server)
            {
                _cond = server.conditions;
                _condInit = true;
            }
        }

        void OnGUI()
        {
            InitStyles();

            // ── Top bar ───────────────────────────────────────────────────────────
            GUI.Box(new Rect(0, 0, Screen.width, 65), "");
            GUI.Label(new Rect(10, 5, 600, 22), "Network Smoothing Techniques Demo", _titleStyle);
            GUI.Label(new Rect(10, 28, 700, 18),
                "Space = Pause  |  R = Reset Metrics  |  Click technique name to Solo/Unsolo");

            if (GUI.Button(new Rect(Screen.width - 170, 10, 80, 30), _paused ? "▶ Resume" : "⏸ Pause"))
                TogglePause();
            if (GUI.Button(new Rect(Screen.width - 85, 10, 80, 30), "↺ Reset"))
                ResetAllMetrics();
            if (GUI.Button(new Rect(Screen.width - 255, 10, 80, 30), _showTuner ? "Tune ✓" : "Tune"))
            {
                _showTuner = !_showTuner;
                if (_showTuner) _showResults = false;
            }

            DrawLeftPanel();
            DrawRightPanel();
            if (_showResults) DrawResults();
            if (_showTuner) DrawTuner();
        }

        void DrawLeftPanel()
        {
            // Fill the screen height and scroll, so the benchmark controls at the bottom are never pushed off-screen.
            _leftPanel.height = Mathf.Max(200f, Screen.height - _leftPanel.y - 10f);
            GUILayout.BeginArea(_leftPanel, "Network Conditions", GUI.skin.window);
            GUILayout.Space(8);
            _leftScroll = GUILayout.BeginScrollView(_leftScroll);

            bool controllable = _condInit && (networkSim || server);
            if (controllable)
            {
                var c = _cond;

                GUILayout.Label($"Latency: {c.latencyMs:F0} ms");
                c.latencyMs = GUILayout.HorizontalSlider(c.latencyMs, 0f, 500f);

                GUILayout.Label($"Jitter: {(networkSim ? "±" : "up to +")}{c.jitterMs:F0} ms");
                c.jitterMs = GUILayout.HorizontalSlider(c.jitterMs, 0f, 200f);

                GUILayout.Label($"Packet Loss: {c.lossPct:F0}%");
                c.lossPct = GUILayout.HorizontalSlider(c.lossPct, 0f, 50f);

                if (!networkSim)
                {
                    GUILayout.Label($"Reorder: {c.scramblePct:F0}%");
                    c.scramblePct = GUILayout.HorizontalSlider(c.scramblePct, 0f, 50f);
                }

                GUILayout.Label($"Send Rate: {c.sendRateHz:F0} Hz");
                c.sendRateHz = GUILayout.HorizontalSlider(c.sendRateHz, 1f, 64f);

                if (!c.Equals(_cond)) ApplyConditions(c);
            }
            else
            {
                GUILayout.Label("Waiting for server...");
            }

            bool hasRef = networkSim || mirrorTransport;
            if (hasRef)
            {
                bool current = networkSim ? networkSim.latencyAdjustedReference : mirrorTransport.latencyAdjustedReference;
                bool adjusted = GUILayout.Toggle(current, " Error vs. true path (not live transform)");
                if (adjusted != current)
                {
                    if (networkSim) networkSim.latencyAdjustedReference = adjusted;
                    else mirrorTransport.latencyAdjustedReference = adjusted;
                    ResetAllMetrics();
                }
            }

            if (server)
            {
                GUILayout.Label($"Server Speed: {server.timeScale:F2}x");
                server.timeScale = GUILayout.HorizontalSlider(server.timeScale, 0.1f, 5f);

                server.allowBackMove = GUILayout.Toggle(server.allowBackMove, " Allow Back-Move");
                server.playerControlled = GUILayout.Toggle(server.playerControlled, " Player Controlled (WASD)");
            }

            if (controllable)
            {
                GUILayout.Space(8);
                GUILayout.Label("── Presets ──", _headerStyle);
                var presets = NetPresets.All;
                for (int i = 0; i < presets.Length; i += 3)
                {
                    GUILayout.BeginHorizontal();
                    for (int j = i; j < i + 3 && j < presets.Length; j++)
                        if (GUILayout.Button(presets[j].name))
                        {
                            ApplyConditions(presets[j].conditions);
                            ResetAllMetrics();
                        }

                    GUILayout.EndHorizontal();
                }

                if (networkSim)
                {
                    GUILayout.Space(6);
                    GUILayout.Label(
                        $"Packets sent: {networkSim.packetsSentTotal}  Dropped: {networkSim.packetsDroppedTotal}");
                }

                GUILayout.Space(8);
                GUILayout.Label("── Benchmark ──", _headerStyle);
                if (!networkSim && NetworkServer.active && NetworkClient.active)
                    GUILayout.Label("Host mode: the local client bypasses latency/loss. Run from the ParrelSync client.");
                if (_bench.Running)
                {
                    GUILayout.Label(_bench.Status);
                    if (GUILayout.Button("Cancel")) _bench.Cancel();
                }
                else
                {
                    if (GUILayout.Button($"▶ Run ({NetPresets.All.Length} × {BenchmarkRunner.Duration:F0}s)"))
                        _bench.Begin();
                    if (!string.IsNullOrEmpty(_bench.Status)) GUILayout.Label(_bench.Status);
                    if (_bench.Results.Count > 0 && GUILayout.Button("Show results"))
                    {
                        _showResults = true;
                        _showTuner = false;
                    }
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawRightPanel()
        {
            GUILayout.BeginArea(_rightPanel, "Technique Metrics", GUI.skin.window);
            GUILayout.Space(4);
            _scroll = GUILayout.BeginScrollView(_scroll);

            for (int i = 0; i < clients.Count; i++)
            {
                var c = clients[i];
                bool isVisible = !_hidden.Contains(i);
                GUI.color = isVisible ? c.color : Color.gray;
                GUILayout.BeginHorizontal();
                bool toggled = GUILayout.Toggle(isVisible, "", GUILayout.Width(20));
                if (toggled != isVisible)
                {
                    if (toggled) _hidden.Remove(i);
                    else _hidden.Add(i);
                }

                GUILayout.Label(c.techniqueName);

                GUILayout.EndHorizontal();
                GUI.color = Color.white;

                GUILayout.BeginHorizontal();
                GUILayout.Label($"  Now:  {c.currentError:F3}u", GUILayout.Width(110));
                GUILayout.Label($"  Avg:  {c.averageError:F3}u");
                GUILayout.EndHorizontal();
                GUILayout.Label($"  Max:  {c.maxError:F3}u   ({(c.gameObject.activeSelf ? "active" : "hidden")})");
                GUILayout.Space(4);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawTuner()
        {
            var rect = new Rect(290, 70, 340, Screen.height - 90);
            GUILayout.BeginArea(rect, "Tune technique parameters (live)", GUI.skin.window);
            GUILayout.Space(6);
            if (clients.Count > 0)
            {
                _tunerIndex = Mathf.Clamp(_tunerIndex, 0, clients.Count - 1);
                var names = new string[clients.Count];
                for (int i = 0; i < clients.Count; i++) names[i] = clients[i].techniqueName;
                _tunerIndex = GUILayout.SelectionGrid(_tunerIndex, names, 1);

                GUILayout.Space(6);
                var c = clients[_tunerIndex];
                GUILayout.Label($"{c.techniqueName}   (added delay {c.AddedDelaySeconds * 1000f:F0} ms)", _headerStyle);
                _tunerScroll = GUILayout.BeginScrollView(_tunerScroll);
                _tuner.Draw(c);
                GUILayout.EndScrollView();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Reset to defaults")) _tuner.Reset(c);
                if (GUILayout.Button("Reset metrics")) ResetAllMetrics();
                GUILayout.EndHorizontal();
            }

            if (GUILayout.Button("Close")) _showTuner = false;
            GUILayout.EndArea();
        }

        void DrawResults()
        {
            // The table needs ~740 px. Scale the whole window down when the screen is narrower, so nothing is cut off.
            const float needWidth = 740f;
            float availWidth = Screen.width - 290f;
            float scale = Mathf.Clamp(availWidth / needWidth, 0.5f, 1f);
            var oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            var rect = new Rect(280f / scale, 70f / scale, availWidth / scale, (Screen.height - 90f) / scale);
            GUILayout.BeginArea(rect, "Benchmark results (error in world units)", GUI.skin.window);
            GUILayout.Space(6);
            if (!string.IsNullOrEmpty(_bench.LastFile))
                GUILayout.Label("Saved: " + _bench.LastFile);

            // Score = adjusted error + dvWeight * RMS dv + delayWeight * delay(ms). Re-ranked live; the CSV keeps the defaults.
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Roughness {_dvWeight:F3}", GUILayout.Width(110));
            _dvWeight = GUILayout.HorizontalSlider(_dvWeight, 0f, 0.1f, GUILayout.Width(90));
            GUILayout.Space(10);
            GUILayout.Label($"Delay {_delayWeight:F4}/ms", GUILayout.Width(120));
            _delayWeight = GUILayout.HorizontalSlider(_delayWeight, 0f, 0.01f, GUILayout.Width(90));
            if (GUILayout.Button("Defaults", GUILayout.Width(70)))
            {
                _dvWeight = BenchmarkRunner.DefaultDvWeight;
                _delayWeight = BenchmarkRunner.DefaultDelayWeight;
            }

            GUILayout.EndHorizontal();

            _resultsScroll = GUILayout.BeginScrollView(_resultsScroll, false, false);

            // Overall: mean score over all presets.
            var totals = new Dictionary<string, float>();
            var order = new List<string>();
            foreach (var r in _bench.Results)
                foreach (var row in r.rows)
                {
                    if (!totals.ContainsKey(row.technique)) { totals[row.technique] = 0f; order.Add(row.technique); }
                    totals[row.technique] += BenchmarkRunner.Score(row, _dvWeight, _delayWeight) / _bench.Results.Count;
                }

            order.Sort((x, y) => totals[x].CompareTo(totals[y]));
            GUILayout.Label("Overall (mean score over all presets)", _headerStyle);
            for (int i = 0; i < order.Count; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{i + 1}.", GUILayout.Width(26));
                GUILayout.Label(order[i], GUILayout.Width(190));
                GUILayout.Label($"{totals[order[i]]:F2}", GUILayout.Width(52));
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(10);

            foreach (var r in _bench.Results)
            {
                var c = r.preset.conditions;
                GUILayout.Label(
                    $"{r.preset.name}   {c.latencyMs:F0}ms  ±{c.jitterMs:F0}  loss {c.lossPct:F0}%  rx {r.rxHz:F1} Hz",
                    _headerStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Technique", GUILayout.Width(190));
                GUILayout.Label("Mean", GUILayout.Width(52));
                GUILayout.Label("P95", GUILayout.Width(52));
                GUILayout.Label("Max", GUILayout.Width(52));
                GUILayout.Label("Delay", GUILayout.Width(52));
                GUILayout.Label("AdjMean", GUILayout.Width(52));
                GUILayout.Label("AdjP95", GUILayout.Width(52));
                GUILayout.Label("RMSdv", GUILayout.Width(52));
                GUILayout.Label("Stutter", GUILayout.Width(52));
                GUILayout.Label("Score", GUILayout.Width(52));
                GUILayout.EndHorizontal();
                var ranked = new List<BenchmarkRunner.Row>(r.rows);
                for (int i = 0; i < ranked.Count; i++)
                {
                    var rr = ranked[i];
                    rr.score = BenchmarkRunner.Score(rr, _dvWeight, _delayWeight);
                    ranked[i] = rr;
                }

                ranked.Sort((x, y) => x.score.CompareTo(y.score));
                foreach (var row in ranked)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(row.technique, GUILayout.Width(190));
                    GUILayout.Label($"{row.mean:F3}", GUILayout.Width(52));
                    GUILayout.Label($"{row.p95:F3}", GUILayout.Width(52));
                    GUILayout.Label($"{row.max:F3}", GUILayout.Width(52));
                    GUILayout.Label($"{row.delayMs:F0}ms", GUILayout.Width(52));
                    GUILayout.Label($"{row.adjMean:F3}", GUILayout.Width(52));
                    GUILayout.Label($"{row.adjP95:F3}", GUILayout.Width(52));
                    GUILayout.Label($"{row.rmsDv:F1}", GUILayout.Width(52));
                    GUILayout.Label($"{row.stutters}", GUILayout.Width(52));
                    GUILayout.Label($"{row.score:F2}", GUILayout.Width(52));
                    GUILayout.EndHorizontal();
                }

                GUILayout.Space(8);
            }

            GUILayout.EndScrollView();
            if (GUILayout.Button("Close")) _showResults = false;
            GUILayout.EndArea();
            GUI.matrix = oldMatrix;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        public NetConditions GetConditions() => _cond;

        /// <summary>Applies conditions locally (simulator) or asks the server to apply them (Mirror).</summary>
        public void ApplyConditions(NetConditions c)
        {
            _cond = c;
            if (networkSim)
            {
                networkSim.baseLatencyMs = c.latencyMs;
                networkSim.jitterMs = c.jitterMs;
                networkSim.packetLossRate = c.lossPct / 100f;
                networkSim.sendRateHz = c.sendRateHz;
                if (server) server.sendRateHz = c.sendRateHz;
            }
            else if (server)
            {
                server.RequestConditions(c);
            }
        }

        /// <summary>Show every technique and unpause, so a benchmark samples all of them.</summary>
        public void PrepareBenchmark()
        {
            _hidden.Clear();
            if (_paused) TogglePause();
        }

        void TogglePause()
        {
            _paused = !_paused;
            Time.timeScale = _paused ? 0f : 1f;
        }

        void ResetAllMetrics()
        {
            foreach (var c in clients) c.ResetMetrics();
            if (!networkSim) return;
            networkSim.packetsSentTotal = 0;
            networkSim.packetsDroppedTotal = 0;
        }

        void InitStyles()
        {
            if (_stylesInit) return;
            _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            _metricStyle = new GUIStyle(GUI.skin.label) { fontSize = 11 };
            _headerStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            _stylesInit = true;
        }
    }
}
