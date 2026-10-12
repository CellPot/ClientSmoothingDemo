using System;
using System.Linq;
using Mirror;
using UnityEngine;

namespace Network
{
    /// <summary>
    /// The authoritative "server" object. Moves along a deterministic path
    /// and broadcasts snapshots via NetworkSimulator at a fixed send rate.
    /// </summary>
    public class ServerEntity : NetworkBehaviour
    {
        // Singleton-style reference for convenience in demo scenes
        public static ServerEntity Instance { get; private set; }
        public static event Action<ServerEntity> OnSpawned;

        public enum DriveMode
        {
            Local,      // Runs locally regardless of networking (used in simulator scene)
            ServerOnly  // Only the server instance drives movement (Mirror scene)
        }

        [Header("Network")]
        [SerializeField] private MonoBehaviour transportBehaviour;
        private INetworkTransport _transport;
        private ITruthSender _truthSender;
        private float _truthTimer;
        private const float TruthInterval = 1f / 60f;
        [SerializeField] public DriveMode driveMode = DriveMode.Local;
        
        [Range(1f, 64f)] public float sendRateHz = 20f;

        [Tooltip("Seed for the kick sequence. Same seed + ResetPath => identical path.")]
        public int pathSeed = 12345;

        // Network conditions, owned by the server and synced so any client UI can show/change them.
        [SyncVar(hook = nameof(OnConditionsChanged))] public NetConditions conditions;
        [Header("Movement")] [Range(0.1f, 5f)] 
        public float timeScale = 1f;
        [Range(1f, 20f)] public float moveSpeed = 5f;
        public float speedX = 2f;
        public float speedY = 1.3f;
        public float amplitudeX = 5f;
        public float amplitudeY = 2.5f;
        public bool addRandomKicks = true;
        public float kickInterval = 3f;
        public bool allowBackMove = true;
        public bool playerControlled = false;

        [HideInInspector] public Vector3 TruePosition { get; private set; }
        [HideInInspector] public Vector3 TrueVelocity { get; private set; }

        private float _sendTimer;
        private Vector3 _prevPos;
        private float _kickTimer;
        private Vector3 _kickOffset;
        private float _kickDecay;
        private float _pathTime;
        private bool _hasPrev;
        private System.Random _rng;
        private Sprite _sprite;
        private Material _trailMaterial;

        void Awake()
        {
            NetDiagnostics.Write($"ServerEntity Awake {DebugState()}");
            ResetPath(pathSeed);
            _transport = transportBehaviour as INetworkTransport ?? FindTransport();
            if (_transport == null)
                Debug.LogWarning($"[{name}] No INetworkTransport found. Server will move but won't send snapshots until a transport is present.", this);
            else
                transportBehaviour = (MonoBehaviour)_transport;
            _truthSender = _transport as ITruthSender;

            // Scene-placed (simulator) entities are never started by Mirror, so announce them here.
            if (driveMode == DriveMode.Local)
            {
                Instance = this;
                OnSpawned?.Invoke(this);
            }

            var sr = gameObject.AddComponent<SpriteRenderer>();
            _sprite = CreateDiamondSprite(Color.white);
            sr.sprite = _sprite;
            sr.sortingOrder = 10;
            gameObject.transform.localScale = Vector3.one * 0.45f;

            // Trail on server so you can see the true path
            var trail = gameObject.AddComponent<TrailRenderer>();
            trail.time = 3f;
            trail.startWidth = 0.08f;
            trail.endWidth = 0.01f;
            _trailMaterial = new Material(Shader.Find("Sprites/Default"));
            trail.material = _trailMaterial;
            trail.startColor = new Color(1f, 1f, 1f, 0.6f);
            trail.endColor = new Color(1f, 1f, 1f, 0f);
        }

        // Called on both server and clients when the object spawns on their side
        public override void OnStartServer()
        {
            base.OnStartServer();
            Instance = this;
            NetDiagnostics.Write($"ServerEntity OnStartServer netId={netId}");
            CaptureConditions();
            OnSpawned?.Invoke(this);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            Instance = this;
            NetDiagnostics.Write($"ServerEntity OnStartClient netId={netId} isServer={isServer}");
            if (!isServer) ApplyToLatencySim(conditions);
            // In host mode OnStartServer already announced this instance.
            if (!isServer) OnSpawned?.Invoke(this);
        }

        public override void OnStopServer()
        {
            if (Instance == this) Instance = null;
            base.OnStopServer();
        }

        public override void OnStopClient()
        {
            if (Instance == this) Instance = null;
            base.OnStopClient();
        }

        public string DebugState() =>
            $"{name} activeInHierarchy={gameObject.activeInHierarchy} enabled={enabled} driveMode={driveMode} " +
            $"hasNetIdentity={netIdentity != null} hasTransport={_transport != null} sendRateHz={sendRateHz} " +
            $"timeScale={timeScale} playerControlled={playerControlled} pathTime={_pathTime:F2} pos={transform.position}";

        // The simulator scene places this object without Mirror starting it, so the NetworkBehaviour
        // accessors (isClient, netId...) must not be trusted unless a NetworkIdentity is present.
        bool IsNetworkedClient => netIdentity != null && isClient;
        string NetIdText => netIdentity != null ? netId.ToString() : "-";

        // ── Seeded path ─────────────────────────────────────────────────────────

        /// <summary>Restart the path from t=0 with a fresh seeded RNG (server side).</summary>
        public void ResetPath(int seed)
        {
            _rng = new System.Random(seed);
            _pathTime = 0f;
            _kickTimer = 0f;
            _kickOffset = Vector3.zero;
            _kickDecay = 0f;
            _hasPrev = false;
        }

        Vector3 RandomInCircle()
        {
            float angle = Rand(0f, Mathf.PI * 2f);
            float radius = Mathf.Sqrt(Rand(0f, 1f));
            return new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
        }

        float Rand(float min, float max) => min + (float)_rng.NextDouble() * (max - min);

        public void RequestPathReset(int seed)
        {
            if (IsNetworkedClient) CmdResetPath(seed);
            else ResetPath(seed);
        }

        [Command(requiresAuthority = false)]
        void CmdResetPath(int seed) => ResetPath(seed);

        // ── Network conditions ──────────────────────────────────────────────────

        public void RequestConditions(NetConditions c)
        {
            if (IsNetworkedClient) CmdSetConditions(c);
            else ServerApplyConditions(c);
        }

        [Command(requiresAuthority = false)]
        void CmdSetConditions(NetConditions c) => ServerApplyConditions(c);

        void ServerApplyConditions(NetConditions c)
        {
            c.latencyMs = Mathf.Clamp(c.latencyMs, 0f, 2000f);
            c.jitterMs = Mathf.Clamp(c.jitterMs, 0f, 1000f);
            c.lossPct = Mathf.Clamp(c.lossPct, 0f, 100f);
            c.scramblePct = Mathf.Clamp(c.scramblePct, 0f, 100f);
            c.sendRateHz = Mathf.Clamp(c.sendRateHz, 1f, 64f);

            sendRateHz = c.sendRateHz;
            if (netIdentity != null && isServer) conditions = c;
            ApplyToLatencySim(c);
        }

        void CaptureConditions()
        {
            var sim = FindFirstObjectByType<LatencySimulation>();
            conditions = new NetConditions
            {
                latencyMs = sim ? sim.latency : 0f,
                jitterMs = sim ? sim.jitter * 1000f : 0f,
                lossPct = sim ? sim.unreliableLoss : 0f,
                scramblePct = sim ? sim.unreliableScramble : 0f,
                sendRateHz = sendRateHz
            };
        }

        // LatencySimulation delays what its own process *sends*: the host's instance shapes server->client
        // traffic, clients' instances shape their uplink (so RTT is symmetric).
        void ApplyToLatencySim(NetConditions c)
        {
            var sim = FindFirstObjectByType<LatencySimulation>();
            if (!sim) return;
            sim.latency = c.latencyMs;
            sim.jitter = c.jitterMs / 1000f;
            sim.unreliableLoss = c.lossPct;
            sim.unreliableScramble = c.scramblePct;
        }

        void OnConditionsChanged(NetConditions oldValue, NetConditions newValue)
        {
            if (!isServer) ApplyToLatencySim(newValue);
        }

        // Same GameObject first, then anywhere in the scene.
        INetworkTransport FindTransport()
        {
            return GetComponents<MonoBehaviour>().OfType<INetworkTransport>().FirstOrDefault()
                   ?? FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).OfType<INetworkTransport>().FirstOrDefault();
        }

        void OnDisable()
        {
            NetDiagnostics.Write("ServerEntity OnDisable");
        }

        void OnDestroy()
        {
            NetDiagnostics.Write($"ServerEntity OnDestroy netId={NetIdText}");
            if (Instance == this) Instance = null;
            if (_sprite != null)
            {
                Destroy(_sprite.texture);
                Destroy(_sprite);
            }
            if (_trailMaterial != null) Destroy(_trailMaterial);
        }

        static Sprite CreateDiamondSprite(Color color)
        {
            int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Vector2 center = Vector2.one * (size / 2f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Abs(x - center.x) / (size / 2f);
                float dy = Mathf.Abs(y - center.y) / (size / 2f);
                float alpha = Mathf.Clamp01(1f - (dx + dy) + 0.05f) > 0.05f ? 1f : 0f;
                tex.SetPixel(x, y, new Color(color.r, color.g, color.b, alpha));
            }

            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f, size);
        }

        void Update()
        {
            // In Mirror scenes, only the server (host) should drive the authoritative motion
            if (driveMode == DriveMode.ServerOnly && !isServer)
                return;

            Vector3 basePos;

            if (playerControlled)
            {
                Vector3 input = new Vector3(
                    Input.GetAxisRaw("Horizontal"),
                    Input.GetAxisRaw("Vertical"),
                    0f
                );
                transform.position += input.normalized * (moveSpeed * timeScale * Time.deltaTime);
                basePos = transform.position;
            }
            else
            {
                _pathTime += Time.deltaTime * timeScale;
                float t = _pathTime;

                basePos = new Vector3(
                    Mathf.Sin(t * speedX) * amplitudeX,
                    Mathf.Sin(t * speedY) * amplitudeY,
                    0f
                );

                if (addRandomKicks)
                {
                    _kickTimer -= Time.deltaTime;
                    if (_kickTimer <= 0f)
                    {
                        _kickTimer = kickInterval + Rand(-1f, 1f);
                        Vector3 currentDir = new Vector3(
                            Mathf.Cos(t * speedX) * speedX * timeScale,
                            Mathf.Cos(t * speedY) * speedY * timeScale,
                            0f
                        ).normalized;
                        bool backMove = allowBackMove && _rng.NextDouble() < 0.5;
                        _kickOffset = backMove ? -currentDir * 2.5f : RandomInCircle() * 1.5f;
                        _kickDecay = 1f;
                    }

                    _kickDecay = Mathf.MoveTowards(_kickDecay, 0f, Time.deltaTime * 1.5f);
                    basePos += _kickOffset * _kickDecay;
                }
            }

            // First frame has no previous position; avoid a huge bogus velocity.
            TrueVelocity = _hasPrev && Time.deltaTime > 0f ? (basePos - _prevPos) / Time.deltaTime : Vector3.zero;
            _hasPrev = true;
            _prevPos = basePos;
            TruePosition = basePos;
            transform.position = basePos;

            if (_truthSender != null)
            {
                _truthTimer += Time.deltaTime;
                if (_truthTimer >= TruthInterval)
                {
                    _truthTimer = 0f;
                    _truthSender.SendTruth(Time.time, TruePosition);
                }
            }

            // Network broadcast (if a transport is available)
            if (_transport != null)
            {
                _sendTimer += Time.deltaTime;
                float sendInterval = 1f / sendRateHz;
                if (_sendTimer >= sendInterval)
                {
                    _sendTimer -= sendInterval;
                    _transport.Send(new NetworkSimulator.Snapshot
                    {
                        timestamp = Time.time,
                        position = TruePosition,
                        velocity = TrueVelocity
                    });
                }
            }
        }
    }
}