using System.Collections.Generic;
using Mirror;
using Network;
using Techniques;
using UnityEngine;

namespace UI
{
    /// <summary>
    /// Renders the demo overlay UI:
    /// - Left panel: network condition sliders
    /// - Right panel: per-technique error metrics
    /// - Top: pause / reset / solo controls
    /// </summary>
    public class DemoUIManager : MonoBehaviour
    {
        [Header("References")] public NetworkSimulator networkSim;
        [Tooltip("Mirror scene: latency/jitter/loss injected on the wrapped transport. Auto-found if empty.")]
        public LatencySimulation mirrorLatency;
        public MirrorNetworkTransport mirrorTransport;
        public List<BaseClientEntity> clients;
        public ServerEntity server;

        // UI state
        private bool _paused;
        private HashSet<int> _hidden = new HashSet<int>();
        private Rect _leftPanel = new Rect(10, 60, 260, 470);
        private Rect _rightPanel;
        private Vector2 _scroll;

        private GUIStyle _titleStyle;
        private GUIStyle _metricStyle;
        private GUIStyle _headerStyle;
        private bool _stylesInit;

        void Start()
        {
            _rightPanel = new Rect(Screen.width - 280, 60, 270, clients.Count * 100 + 30);
            if (networkSim == null && mirrorLatency == null)
                mirrorLatency = FindFirstObjectByType<LatencySimulation>();
            if (mirrorTransport == null)
                mirrorTransport = FindFirstObjectByType<MirrorNetworkTransport>();
            // If the ServerEntity is prefab-spawned by DemoNetworkManager, pick it up here
            if (server == null)
                server = ServerEntity.Instance;
        }

        void Update()
        {
            // Late assign in case server spawned after this component started
            if (server == null && ServerEntity.Instance != null)
                server = ServerEntity.Instance;

            _rightPanel = new Rect(Screen.width - 280, 60, 270, clients.Count * 100 + 40);

            // Apply solo visibility
            for (int i = 0; i < clients.Count; i++)
                clients[i].gameObject.SetActive(!_hidden.Contains(i));

            // Keyboard shortcut: Space = pause, R = reset metrics
            if (Input.GetKeyDown(KeyCode.Space)) TogglePause();
            if (Input.GetKeyDown(KeyCode.R)) ResetAllMetrics();
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

            // ── Left panel: network sliders ───────────────────────────────────────
            GUILayout.BeginArea(_leftPanel, "Network Conditions", GUI.skin.window);
            GUILayout.Space(8);

            if (networkSim)
            {
                GUILayout.Label($"Latency: {networkSim.baseLatencyMs:F0} ms");
                networkSim.baseLatencyMs = GUILayout.HorizontalSlider(networkSim.baseLatencyMs, 0f, 500f);

                GUILayout.Label($"Jitter: ±{networkSim.jitterMs:F0} ms");
                networkSim.jitterMs = GUILayout.HorizontalSlider(networkSim.jitterMs, 0f, 200f);

                GUILayout.Label($"Packet Loss: {networkSim.packetLossRate * 100:F0}%");
                networkSim.packetLossRate = GUILayout.HorizontalSlider(networkSim.packetLossRate, 0f, 0.5f);

                GUILayout.Label($"Send Rate: {networkSim.sendRateHz:F0} Hz");
                networkSim.sendRateHz = GUILayout.HorizontalSlider(networkSim.sendRateHz, 1f, 64f);
            }

            if (mirrorLatency)
            {
                GUILayout.Label($"Latency: {mirrorLatency.latency:F0} ms");
                mirrorLatency.latency = GUILayout.HorizontalSlider(mirrorLatency.latency, 0f, 500f);

                // LatencySimulation jitter is a perlin spike in seconds (0..jitter)
                GUILayout.Label($"Jitter: up to +{mirrorLatency.jitter * 1000:F0} ms");
                mirrorLatency.jitter = GUILayout.HorizontalSlider(mirrorLatency.jitter, 0f, 0.2f);

                GUILayout.Label($"Packet Loss: {mirrorLatency.unreliableLoss:F0}%");
                mirrorLatency.unreliableLoss = GUILayout.HorizontalSlider(mirrorLatency.unreliableLoss, 0f, 50f);

                GUILayout.Label($"Reorder: {mirrorLatency.unreliableScramble:F0}%");
                mirrorLatency.unreliableScramble = GUILayout.HorizontalSlider(mirrorLatency.unreliableScramble, 0f, 50f);
            }

            if (mirrorTransport)
            {
                bool adjusted = GUILayout.Toggle(mirrorTransport.latencyAdjustedReference, " Error vs. true path (not live transform)");
                if (adjusted != mirrorTransport.latencyAdjustedReference)
                {
                    mirrorTransport.latencyAdjustedReference = adjusted;
                    ResetAllMetrics();
                }
            }

            if (server)
            {
                if (mirrorLatency)
                {
                    GUILayout.Label($"Send Rate: {server.sendRateHz:F0} Hz");
                    server.sendRateHz = GUILayout.HorizontalSlider(server.sendRateHz, 1f, 64f);
                }

                GUILayout.Label($"Server Speed: {server.timeScale:F2}x");
                server.timeScale = GUILayout.HorizontalSlider(server.timeScale, 0.1f, 5f);

                server.allowBackMove = GUILayout.Toggle(server.allowBackMove, " Allow Back-Move");
                server.playerControlled = GUILayout.Toggle(server.playerControlled, " Player Controlled (WASD)");
            }
            else
            {
                GUILayout.Label("Server: (spawning...)");
            }
            if (networkSim || mirrorLatency)
            {
                GUILayout.Space(8);
                GUILayout.Label("── Presets ──", _headerStyle);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Perfect")) ApplyPreset(0, 0, 0, 20);
                if (GUILayout.Button("LAN")) ApplyPreset(10, 5, 0, 20);
                if (GUILayout.Button("Broadband")) ApplyPreset(80, 20, 0.02f, 20);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Mobile")) ApplyPreset(150, 60, 0.05f, 10);
                if (GUILayout.Button("Lossy")) ApplyPreset(200, 100, 0.15f, 10);
                if (GUILayout.Button("Chaos")) ApplyPreset(300, 150, 0.25f, 5);
                GUILayout.EndHorizontal();

                if (networkSim)
                {
                    GUILayout.Space(6);
                    GUILayout.Label(
                        $"Packets sent: {networkSim.packetsSentTotal}  Dropped: {networkSim.packetsDroppedTotal}");
                }
            }

            GUILayout.EndArea();

            // ── Right panel: per-technique metrics ────────────────────────────────
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

        // ── Helpers ───────────────────────────────────────────────────────────────

        void ApplyPreset(float lat, float jitter, float loss, float rate)
        {
            if (networkSim)
            {
                networkSim.baseLatencyMs = lat;
                networkSim.jitterMs = jitter;
                networkSim.packetLossRate = loss;
                networkSim.sendRateHz = rate;
            }
            else if (mirrorLatency)
            {
                mirrorLatency.latency = lat;
                mirrorLatency.jitter = jitter / 1000f;
                mirrorLatency.unreliableLoss = loss * 100f;
                if (server) server.sendRateHz = rate;
            }
            else return;

            ResetAllMetrics();
        }

        void TogglePause()
        {
            _paused = !_paused;
            Time.timeScale = _paused ? 0f : 1f;
        }

        void ResetAllMetrics()
        {
            if (!networkSim) return;

            foreach (var c in clients) c.ResetMetrics();
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