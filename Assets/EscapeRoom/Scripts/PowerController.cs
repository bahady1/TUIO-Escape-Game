using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace EscapeRoom
{
    /// <summary>
    /// Building power. Power off = ceiling lamps off, dim red emergency lights on, very dark ambient.
    /// The intro dialogue cuts the power; solving the switchbox (puzzle 1) restores it.
    /// </summary>
    public class PowerController : MonoBehaviour
    {
        public static PowerController Instance { get; private set; }

        [Header("Ceiling lamps (on with power)")]
        public List<Light> lamps = new();
        public List<Renderer> lampRenderers = new();
        [Tooltip("Material the lamp panels get while the power is off.")]
        public Material lampOffMaterial;

        [Header("Emergency lighting (on without power)")]
        public List<Light> emergencyLights = new();
        public List<Renderer> emergencyRenderers = new();

        [Header("Ambient light")]
        public Color ambientPowerOn = new(0.20f, 0.20f, 0.23f);
        public Color ambientPowerOff = new(0.045f, 0.045f, 0.06f);

        public bool startPowered = true;

        public bool PowerOn { get; private set; }
        public event Action<bool> PowerChanged;

        readonly Dictionary<Renderer, Material> _lampOnMaterials = new();
        Coroutine _routine;

        void Awake()
        {
            Instance = this;
            foreach (var r in lampRenderers)
                if (r != null) _lampOnMaterials[r] = r.sharedMaterial;
            Apply(startPowered);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Power failure (with a short flicker).</summary>
        public void CutPower(bool flicker = true) => Switch(false, flicker);

        /// <summary>Power back on (with a short flicker).</summary>
        public void RestorePower(bool flicker = true) => Switch(true, flicker);

        void Switch(bool on, bool flicker)
        {
            if (_routine != null) StopCoroutine(_routine);
            if (!flicker || !isActiveAndEnabled)
            {
                Apply(on);
                return;
            }
            _routine = StartCoroutine(Flicker(on));
        }

        IEnumerator Flicker(bool target)
        {
            // A few irregular blinks, then settle on the target state.
            float[] waits = { 0.08f, 0.12f, 0.06f, 0.25f, 0.05f, 0.15f };
            bool state = !target;
            foreach (var w in waits)
            {
                state = !state;
                ApplyLights(state);
                yield return new WaitForSeconds(w);
            }
            Apply(target);
            _routine = null;
        }

        void Apply(bool on)
        {
            ApplyLights(on);
            bool changed = PowerOn != on;
            PowerOn = on;
            if (changed) PowerChanged?.Invoke(on);
        }

        void ApplyLights(bool on)
        {
            foreach (var l in lamps) if (l != null) l.enabled = on;
            foreach (var r in lampRenderers)
            {
                if (r == null) continue;
                if (on && _lampOnMaterials.TryGetValue(r, out var m)) r.sharedMaterial = m;
                else if (!on && lampOffMaterial != null) r.sharedMaterial = lampOffMaterial;
            }
            foreach (var l in emergencyLights) if (l != null) l.enabled = !on;
            foreach (var r in emergencyRenderers) if (r != null) r.enabled = !on;
            RenderSettings.ambientLight = on ? ambientPowerOn : ambientPowerOff;
        }
    }
}
