using UnityEngine;

namespace EscapeRoom
{
    /// <summary>Randomly blinks a renderer (server status LEDs, warning lamp...).</summary>
    public class BlinkingLight : MonoBehaviour
    {
        public float minInterval = 0.15f;
        public float maxInterval = 1.2f;
        [Range(0f, 1f)] public float onChance = 0.7f;

        Renderer _renderer;
        Light _light;
        float _next;

        void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _light = GetComponent<Light>();
        }

        void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + Random.Range(minInterval, maxInterval);
            bool on = Random.value < onChance;
            if (_renderer != null) _renderer.enabled = on;
            if (_light != null) _light.enabled = on;
        }
    }
}
