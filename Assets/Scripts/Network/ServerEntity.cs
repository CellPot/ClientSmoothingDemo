using UnityEngine;

namespace Network
{
    /// <summary>
    /// The authoritative "server" object. Moves along a deterministic path
    /// and broadcasts snapshots via NetworkSimulator at a fixed send rate.
    /// </summary>
    public class ServerEntity : MonoBehaviour
    {
        [Header("Network")]
        [SerializeField] private MonoBehaviour transportBehaviour;
        private INetworkTransport _transport;
        
        [Range(1f, 64f)] public float sendRateHz = 20f;
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

        void Awake()
        {
            _transport = transportBehaviour as INetworkTransport;
            if (_transport == null)
                Debug.LogError($"[{name}] transportBehaviour does not implement INetworkTransport.", this);

            
            var sr = gameObject.AddComponent<SpriteRenderer>();
            sr.sprite = CreateDiamondSprite(Color.white);
            sr.sortingOrder = 10;
            gameObject.transform.localScale = Vector3.one * 0.45f;

            // Trail on server so you can see the true path
            var trail = gameObject.AddComponent<TrailRenderer>();
            trail.time = 3f;
            trail.startWidth = 0.08f;
            trail.endWidth = 0.01f;
            trail.material = new Material(Shader.Find("Sprites/Default"));
            trail.startColor = new Color(1f, 1f, 1f, 0.6f);
            trail.endColor = new Color(1f, 1f, 1f, 0f);
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
                        _kickTimer = kickInterval + Random.Range(-1f, 1f);
                        Vector3 currentDir = new Vector3(
                            Mathf.Cos(t * speedX) * speedX * timeScale,
                            Mathf.Cos(t * speedY) * speedY * timeScale,
                            0f
                        ).normalized;
                        bool backMove = allowBackMove && Random.value < 0.5f;
                        _kickOffset = backMove ? -currentDir * 2.5f : Random.insideUnitCircle * 1.5f;
                        _kickDecay = 1f;
                    }

                    _kickDecay = Mathf.MoveTowards(_kickDecay, 0f, Time.deltaTime * 1.5f);
                    basePos += _kickOffset * _kickDecay;
                }
            }

            TrueVelocity = (basePos - _prevPos) / Time.deltaTime;
            _prevPos = basePos;
            TruePosition = basePos;
            transform.position = basePos;

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