using Techniques;
using UnityEngine;

namespace UI
{
    /// <summary>
    /// Draws a colour-coded position trail behind a client entity so you can
    /// visually compare how closely each technique follows the server path.
    /// Attach alongside any BaseClientEntity component.
    /// </summary>
    [RequireComponent(typeof(BaseClientEntity))]
    public class TechniqueTrail : MonoBehaviour
    {
        [Range(0.5f, 5f)]  public float trailTime = 2f;
        [Range(0.02f, 0.2f)] public float trailWidth = 0.06f;

        private TrailRenderer _trail;
        private BaseClientEntity _client;

        void Start()
        {
            _client = GetComponent<BaseClientEntity>();

            _trail = gameObject.AddComponent<TrailRenderer>();
            _trail.time        = trailTime;
            _trail.startWidth  = trailWidth;
            _trail.endWidth    = 0.01f;
            _trail.material    = new Material(Shader.Find("Sprites/Default"));
            _trail.startColor  = _client.color;
            _trail.endColor    = new Color(_client.color.r, _client.color.g, _client.color.b, 0f);
            _trail.numCapVertices   = 4;
            _trail.numCornerVertices = 4;
            _trail.generateLightingData = false;
        }
    }
}
