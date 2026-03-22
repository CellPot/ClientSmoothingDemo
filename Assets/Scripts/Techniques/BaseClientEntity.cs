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
        public ServerEntity server;

        [Header("Display")] public string techniqueName = "Base";
        public Color color = Color.white;

        // Error metrics
        [HideInInspector] public float currentError;
        [HideInInspector] public float averageError;
        [HideInInspector] public float maxError;

        private float _errorAccum;
        private int _errorSamples;

        protected NetworkSimulator.Snapshot latestSnapshot;
        protected bool hasSnapshot;

        protected virtual void OnEnable()
        {
            _transport?.RegisterListener(OnSnapshotReceived);
        }

        protected virtual void OnDisable()
        {
            _transport?.UnregisterListener(OnSnapshotReceived);
        }

        protected virtual void Awake()
        {
            _transport = transportBehaviour as INetworkTransport;

            if (_transport == null)
                Debug.LogError($"[{name}] transportBehaviour does not implement INetworkTransport.", this);


            var go = this.gameObject;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = CreateCircleSprite(color);
            sr.sortingOrder = 5;
            go.transform.localScale = Vector3.one * 0.3f;

            go.AddComponent<TechniqueTrail>();
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
            if (server == null) return;
            currentError = Vector3.Distance(transform.position, server.TruePosition);
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