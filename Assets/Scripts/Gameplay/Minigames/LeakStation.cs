using UnityEngine;
using UnityEngine.UIElements;

namespace FishGame
{
    /// <summary>
    /// A rusty drum on the seabed leaking oil. The minigame: the pressure needle drifts on its own; tap A to
    /// vent and D to pump and hold it inside the green band. While it's in the band the seal builds - and
    /// the band narrows as it does. Out in the red too long and it bursts: a cloud of oil, and the seal
    /// slips back. A full seal stops the leak for good.
    /// </summary>
    public class LeakStation : MinigameStation
    {
        protected override string Layout => "Leak";
        protected override string Prompt => "seal the leak";
        protected override string TaskName => "Leak sealed";
        protected override float RewardSeconds => 20f;
        protected override bool UsesSideKeys => true;
        protected override string DoneWord => "PLUGGED";

        const float SealSeconds = 9f;      // in the band for this long seals it
        const float BurstAfter = 1.3f;     // seconds out of the band before it bursts
        const float Tap = 0.16f;           // one A / D tap's push on the needle (per tap, as velocity)

        // looks
        Transform _oilRoot;
        readonly Transform[] _blobs = new Transform[12];
        readonly float[] _blobAge = new float[12];
        Transform _patch, _slick;
        float _burstUntil;

        // game
        PressureGauge _gauge;
        Label _vent, _pump;
        float _value, _vel, _seal, _outFor, _noise;

        protected override void BuildLooks()
        {
            var art = MinigameArt.Get();
            var body = new GameObject("Drum").transform;
            body.SetParent(transform, false);
            const float S = 1.4f;                        // a bit bigger than life, so it reads at a distance
            // on its side, half sunk in the sand, the lid end toward +x
            var drum = MinigameArt.Spawn(art.drum, body, new Vector3(-0.44f * S, 0.24f * S, 0f), Quaternion.Euler(0f, 0f, -90f), S);
            if (drum == null)   // no prop assigned: the old stand-in
                Part(PrimitiveType.Cylinder, body, new Vector3(0f, 0.42f, 0f), new Vector3(0.9f, 0.65f, 0.9f), art.rust, Quaternion.Euler(0f, 0f, 90f));
            float top = 0.54f * S;                       // the top of the drum's side, where it split
            // a riveted steel patch over the split once sealed
            _patch = Part(PrimitiveType.Cube, body, new Vector3(0.1f, top + 0.01f, 0f), new Vector3(0.34f, 0.025f, 0.26f), art.rust);
            MinigameArt.Tint(_patch.GetComponent<Renderer>(), new Color(0.45f, 0.47f, 0.5f));
            _oilRoot = new GameObject("Oil").transform;
            _oilRoot.SetParent(transform, false);
            _oilRoot.localPosition = new Vector3(0.1f, top, 0f);
            for (int i = 0; i < _blobs.Length; i++)
            {
                _blobs[i] = Part(PrimitiveType.Sphere, _oilRoot, Vector3.zero, Vector3.one * 0.12f, art.plastic);
                MinigameArt.Tint(_blobs[i].GetComponent<Renderer>(), new Color(0.03f, 0.03f, 0.03f));
                _blobAge[i] = i / (float)_blobs.Length * 3f;
            }
        }

        protected override void ShowDone(bool done)
        {
            if (_patch != null) _patch.gameObject.SetActive(done);
            if (_oilRoot != null) _oilRoot.gameObject.SetActive(!done);
            if (_slick != null) _slick.gameObject.SetActive(!done);
        }

        protected override void Update()
        {
            base.Update();
            if (Done || _oilRoot == null) return;
            // oil wobbling up from the crack - a thick plume after a burst
            float rate = Time.time < _burstUntil ? 3f : 1f;
            for (int i = 0; i < _blobs.Length; i++)
            {
                _blobAge[i] += Time.deltaTime * rate;
                if (_blobAge[i] > 3f) _blobAge[i] -= 3f;
                float a = _blobAge[i];
                float wobble = Mathf.Sin(a * 3f + i) * 0.12f;
                _blobs[i].localPosition = new Vector3(wobble, a * 0.9f, Mathf.Cos(a * 2.3f + i) * 0.1f);
                _blobs[i].localScale = Vector3.one * (0.08f + a * 0.07f) * (rate > 1f ? 1.8f : 1f) * Mathf.Clamp01(3f - a);
            }
        }

        protected override void BeginGame(VisualElement ui)
        {
            _gauge = ui.Q<PressureGauge>("gauge");
            _vent = ui.Q<Label>("vent-key");
            _pump = ui.Q<Label>("pump-key");
            if (_vent != null) _vent.text = MinigameHud.KeyText(GameInput.PuzzleLeft);
            if (_pump != null) _pump.text = MinigameHud.KeyText(GameInput.PuzzleRight);
            _value = 0.5f; _vel = 0f; _seal = 0f; _outFor = 0f;
            _noise = Random.value * 50f;
            MinigameHud.SetHint("Keep the needle between the orange lines and the bolts tighten");
        }

        protected override void TickGame(float dt)
        {
            float band = Mathf.Lerp(0.32f, 0.1f, _seal);   // narrows as the seal builds

            // the pressure wanders, harder the more sealed it is
            float t = Time.time * 0.7f;
            float push = (Mathf.PerlinNoise(_noise, t) - 0.5f) * 2f * Mathf.Lerp(0.55f, 1f, _seal);
            if (GameInput.PuzzleLeft.WasPressedThisFrame()) _vel -= Tap;
            if (GameInput.PuzzleRight.WasPressedThisFrame()) _vel += Tap;
            _vel += push * dt * 0.9f;
            _vel *= Mathf.Exp(-1.6f * dt);
            _value += _vel * dt * 2.2f;
            if (_value < 0f || _value > 1f) { _value = Mathf.Clamp01(_value); _vel = 0f; }

            bool inBand = Mathf.Abs(_value - 0.5f) <= band / 2f;
            if (inBand) { _seal = Mathf.Min(1f, _seal + dt / SealSeconds); _outFor = 0f; }
            else _outFor += dt;

            if (_outFor >= BurstAfter)
            {
                _outFor = 0f;
                _seal = Mathf.Max(0f, _seal - 0.25f);
                _value = 0.5f; _vel = 0f;
                _burstUntil = Time.time + 2.5f;
                MinigameHud.Flash("BURST!", false);
                GameFeel.Shake(0.2f);
                WaterFx.BubbleBurst(transform.position + Vector3.up, 20, 0.5f, 1.4f, new Vector2(0.03f, 0.08f));
            }

            _gauge.value = _value;
            _gauge.bandWidth = band;
            _gauge.danger = _outFor / BurstAfter;
            _gauge.seal = _seal;
            _gauge.MarkDirtyRepaint();

            if (_seal >= 1f) Solve();
        }
    }
}
