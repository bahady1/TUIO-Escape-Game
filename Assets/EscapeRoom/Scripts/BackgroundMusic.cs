using System.Collections;
using UnityEngine;

namespace EscapeRoom
{
    /// <summary>
    /// Looping background music. Fades in at start, gets slightly louder and faster in the last minute,
    /// and fades out when the time runs out. Swap the clip for any other track in the inspector.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class BackgroundMusic : MonoBehaviour
    {
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 0.45f;
        public float fadeInSeconds = 3f;

        [Header("Last minute")]
        [Tooltip("Speed the music up a little when the timer is below the HUD's warning time.")]
        public bool tenseLastMinute = true;
        public float lastMinutePitch = 1.08f;
        public float lastMinuteVolumeBoost = 1.2f;

        public float fadeOutOnTimeUpSeconds = 4f;

        AudioSource _source;
        float _level;        // 0..1 fade multiplier
        bool _fadingOut;

        void Awake()
        {
            _source = GetComponent<AudioSource>();
            _source.clip = clip;
            _source.loop = true;
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.volume = 0f;
        }

        void Start()
        {
            if (clip == null) return;
            _source.Play();
            if (GameHUD.Instance != null) GameHUD.Instance.TimeUp += OnTimeUp;
        }

        void OnDestroy()
        {
            if (GameHUD.Instance != null) GameHUD.Instance.TimeUp -= OnTimeUp;
        }

        void OnTimeUp()
        {
            _fadingOut = true;
        }

        void Update()
        {
            if (_fadingOut)
                _level = Mathf.MoveTowards(_level, 0f, Time.deltaTime / Mathf.Max(0.01f, fadeOutOnTimeUpSeconds));
            else
                _level = Mathf.MoveTowards(_level, 1f, Time.deltaTime / Mathf.Max(0.01f, fadeInSeconds));

            bool tense = false;
            var hud = GameHUD.Instance;
            if (tenseLastMinute && hud != null && hud.IsRunning && hud.RemainingSeconds <= hud.warningSeconds) tense = true;

            float targetPitch = tense ? lastMinutePitch : 1f;
            _source.pitch = Mathf.MoveTowards(_source.pitch, targetPitch, Time.deltaTime * 0.05f);
            _source.volume = volume * _level * (tense ? lastMinuteVolumeBoost : 1f);
        }

        /// <summary>Fade the music out (e.g. when the room is solved).</summary>
        public void FadeOut(float seconds = 3f)
        {
            fadeOutOnTimeUpSeconds = seconds;
            _fadingOut = true;
        }

        /// <summary>Fade the music back in.</summary>
        public void FadeIn()
        {
            _fadingOut = false;
            if (!_source.isPlaying && clip != null) _source.Play();
        }
    }
}
