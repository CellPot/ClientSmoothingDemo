using UnityEngine;

namespace Network
{
    /// <summary>
    /// The authoritative "server" object. Moves along a deterministic path
    /// and broadcasts snapshots via NetworkSimulator at a fixed send rate.
    /// </summary>
    public class ServerEntity : MonoBehaviour
    {
        [Header("Movement")] [Range(0.1f, 5f)] public float timeScale = 1f;
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
        private INetworkTransport _transport;

        void Awake()
        {
            _transport = NetworkSimulator.Instance;
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
            float sendInterval = 1f / NetworkSimulator.Instance.sendRateHz;
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