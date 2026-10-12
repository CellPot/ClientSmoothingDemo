using Network;
using UI;
using UnityEngine;

namespace Techniques
{
    /// <summary>
    /// Base class for all client smoothing technique implementations.
    /// Handles snapshot registration, position error tracking, and display.
    /// </summary>
    public abstract class BaseClientEntity : MonoBehaviour
    {
        [Header("References")] [SerializeField]
        private MonoBehaviour transportBehaviour;

        private INetworkTransport _transport;
        private IReferenceSource _reference;
        public ServerEntity server;

        [Header("Display")] public string techniqueName = "Base";
        public Color color = Color.white;

        // Error metrics
        [HideInInspector] public float currentError;
        [HideInInspector] public float averageError;
        [HideInInspector] public float maxError;

        private float _errorAccum;
        private int _errorSamples;
        private Sprite _sprite;

        [HideInInspector] public int snapshotsReceived;

        /// <summary>Intentional delay the technique adds (buffer depth), for benchmark reports.</summary>
        public virtual float AddedDelaySeconds => 0f;

        protected NetworkSimulator.Snapshot latestSnapshot;
        protected bool hasSnapshot;

        protected virtual void OnEnable()
        {
            if (_transport != null) _transport.RegisterListener(OnSnapshotReceived);
            // Late-spawned server support (Mirror prefab spawn):
            if (server == null)
            {
                if (Network.ServerEntity.Instance != null)
                {
                    server = Network.ServerEntity.Instance;
                }
                else
                {
                    Network.ServerEntity.OnSpawned += HandleServerSpawned;
                }
            }
        }

        protected virtual void OnDisable()
        {
            if (_transport != null) _transport.UnregisterListener(OnSnapshotReceived);
            Network.ServerEntity.OnSpawned -= HandleServerSpawned;
        }

        protected virtual void Awake()
        {
            _transport = transportBehaviour as INetworkTransport;
            _reference = transportBehaviour as IReferenceSource;
            if (_transport == null)
                Debug.LogError($"[{name}] transportBehaviour does not implement INetworkTransport.", this);


            var go = this.gameObject;
            var sr = go.AddComponent<SpriteRenderer>();
            _sprite = CreateCircleSprite(color);
            sr.sprite = _sprite;
            sr.sortingOrder = 5;
            go.transform.localScale = Vector3.one * 0.3f;

            go.AddComponent<TechniqueTrail>();
        }

        protected virtual void OnDestroy()
        {
            if (_sprite == null) return;
            Destroy(_sprite.texture);
            Destroy(_sprite);
        }

        private void HandleServerSpawned(Network.ServerEntity srv)
        {
            if (server == null)
            {
                server = srv;
                // once assigned, no need to keep listening
                Network.ServerEntity.OnSpawned -= HandleServerSpawned;
            }
        }

        static Sprite CreateCircleSprite(Color color)
        {
            int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Vector2 center = Vector2.one * (size / 2f);
            float radius = size / 2f - 1f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                float alpha = Mathf.Clamp01(radius - dist + 0.5f);
                tex.SetPixel(x, y, new Color(color.r, color.g, color.b, alpha));
            }

            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f, size);
        }

        private void OnSnapshotReceived(NetworkSimulator.Snapshot snap)
        {
            snapshotsReceived++;
            latestSnapshot = snap;
            hasSnapshot = true;
            OnSnapshot(snap);
        }

        /// <summary>Override to handle incoming snapshot for your technique.</summary>
        protected abstract void OnSnapshot(NetworkSimulator.Snapshot snap);

        protected virtual void Update()
        {
            UpdatePosition();
            TrackError();
        }

        /// <summary>Override to set transform.position each frame.</summary>
        protected abstract void UpdatePosition();

        private void TrackError()
        {
            Vector3 reference;
            if (_reference != null && _reference.TryGetTruth(Time.time, out var truth))
                reference = truth; // Mirror: true path at the matching local time
            else if (server != null)
                reference = server.transform.position; // simulator scene / fallback: live server transform
            else
                return;
            currentError = Vector3.Distance(transform.position, reference);
            _errorAccum += currentError;
            _errorSamples++;
            averageError = _errorAccum / _errorSamples;
            if (currentError > maxError) maxError = currentError;
        }

        public void ResetMetrics()
        {
            currentError = averageError = maxError = 0f;
            _errorAccum = 0f;
            _errorSamples = 0;
        }
    }
}