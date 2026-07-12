using System.Collections;
using CBuilding.Data;
using UnityEngine;

namespace CBuilding.Heroes
{
    /// <summary>
    /// GS-17 §3 — client-side-only oversized weapon sprite rig.
    ///
    /// HIERARCHY:  Hero_&lt;Name&gt; → WeaponPivotSocket (hand/hip anchor) → WeaponVisual
    /// (SpriteRenderer + this). Author sprites pointing local +X (east, 0°); use
    /// spriteForwardOffset for weapons drawn differently instead of re-authoring.
    ///
    /// Reads HeroController.AimDirection every LateUpdate — identical code path on the
    /// owner (fresh mouse aim) and remotes (reconstructed from the throttled 1-byte
    /// NetworkVariable, GS-17 §4). Never touches the network layer itself.
    /// </summary>
    public class WeaponVisualController : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private HeroController owner;
        [Tooltip("The hero's own body SpriteRenderer — sorting is offset relative to its LIVE sortingOrder.")]
        [SerializeField] private SpriteRenderer heroBodySprite;

        [Header("Rotation")]
        [Tooltip("Degrees/second toward the aim angle. High enough to feel responsive, low enough to read as a turn — also smooths the ~1.4° quantized remote aim.")]
        [SerializeField] private float rotationSpeedDeg = 1080f;
        [Tooltip("Add if this sprite wasn't authored pointing +X/east (GS-17 §3 authoring convention).")]
        [SerializeField] private float spriteForwardOffset = 0f;

        [Header("Flip")]
        [Tooltip("Dead-zone on the normalized aim X before flipY toggles — stops flicker when aiming near-vertically.")]
        [SerializeField] private float flipHysteresis = 0.08f;

        [Header("Isometric sorting")]
        [Tooltip("Dead-zone on the normalized aim 'north' component before front/behind swaps.")]
        [SerializeField] private float sortHysteresis = 0.08f;
        [Tooltip("Open Question #3: aim-north on MainIsoCam should mean 'away from camera' (weapon behind). If the 5-minute sign check (log the north component while aiming due north) comes back negative, tick this — one flag, all 8 heroes fixed at once.")]
        [SerializeField] private bool invertNorthSouth = false;

        [Header("Recoil")]
        [Tooltip("Local-space punch distance along the firing direction. Author per weight class: Barriers larger/slower, Gladiators smaller/faster (rec #2 — authored, not formula-ized).")]
        [SerializeField] private float recoilDistance = 0.18f;
        [SerializeField] private float recoilOutDuration = 0.05f;
        [SerializeField] private float recoilReturnDuration = 0.12f;

        private SpriteRenderer _sprite;
        private float _currentAngle;
        private bool _flipped;
        private bool _behindBody;
        private Vector3 _restLocalPos;
        private Coroutine _recoilRoutine;

        private void Awake()
        {
            _sprite = GetComponent<SpriteRenderer>();
            _restLocalPos = transform.localPosition;
            if (owner == null) owner = GetComponentInParent<HeroController>();
            if (owner == null) Debug.LogError("[WeaponVisualController] No HeroController found.", this);
        }

        private void LateUpdate()
        {
            if (owner == null || _sprite == null) return;

            Vector3 aim = owner.AimDirection; // XZ plane, normalized
            if (aim.sqrMagnitude < 0.0001f) return;

            UpdateRotation(aim);
            UpdateFlip(aim);
            UpdateSorting(aim);
        }

        // GS-17 §3 Rotation — MoveTowardsAngle, not an instant snap: a slight, readable
        // turn instead of teleporting to face the target.
        private void UpdateRotation(Vector3 aim)
        {
            // World-space yaw of the aim (XZ plane). Unity yaw is clockwise from +Z, so
            // convert to the math-style angle the sprite's +X convention expects.
            float targetAngle = Mathf.Atan2(aim.z, aim.x) * Mathf.Rad2Deg + spriteForwardOffset;
            _currentAngle = Mathf.MoveTowardsAngle(_currentAngle, targetAngle, rotationSpeedDeg * Time.deltaTime);

            // Billboarded iso sprites rotate around the camera-facing axis; rotating the
            // local Z of a sprite authored +X gives the on-screen sweep we want.
            transform.localRotation = Quaternion.Euler(0f, 0f, _currentAngle);
        }

        // GS-17 §3 Flip — flipY when aim crosses into the left half, with hysteresis.
        private void UpdateFlip(Vector3 aim)
        {
            if (!_flipped && aim.x < -flipHysteresis) _flipped = true;
            else if (_flipped && aim.x > flipHysteresis) _flipped = false;

            _sprite.flipY = _flipped;
        }

        // GS-17 §3 Sorting — north (away from camera) = behind the body, south = in
        // front. The OFFSET direction only changes on the hysteresis transition, but the
        // absolute order is recomputed EVERY frame off the body's live sortingOrder,
        // because iso Y-sorting rewrites that continuously as the hero moves.
        private void UpdateSorting(Vector3 aim)
        {
            if (heroBodySprite == null) return;

            float north = invertNorthSouth ? -aim.z : aim.z;

            if (!_behindBody && north > sortHysteresis) _behindBody = true;
            else if (_behindBody && north < -sortHysteresis) _behindBody = false;

            _sprite.sortingLayerID = heroBodySprite.sortingLayerID;
            _sprite.sortingOrder = heroBodySprite.sortingOrder + (_behindBody ? -1 : +1);
        }

        // ---- Recoil (GS-17 §3) ----

        /// <summary>
        /// Called from HeroController's AttackSwingClientRpc on EVERY client — synced to
        /// the actual server-confirmed attack, not local input prediction.
        /// </summary>
        public void PlayRecoil(Vector3 worldFireDirection)
        {
            if (!isActiveAndEnabled) return;

            // Punch BACKWARD along the fire direction, in the pivot's local space.
            Vector3 localDir = transform.parent != null
                ? transform.parent.InverseTransformDirection(-worldFireDirection.normalized)
                : -worldFireDirection.normalized;

            if (_recoilRoutine != null) StopCoroutine(_recoilRoutine);
            _recoilRoutine = StartCoroutine(RecoilRoutine(localDir * recoilDistance));
        }

        private IEnumerator RecoilRoutine(Vector3 localPunch)
        {
            // Rec #2 — clamp total recoil time below the hero's CURRENT attack cooldown
            // so fast attackers never visually stutter into their next swing. This alone
            // makes high-attack-speed heroes read snappier, no speed formula needed.
            float outDur = recoilOutDuration;
            float backDur = recoilReturnDuration;
            if (owner != null && owner.Stats != null)
            {
                float budget = owner.Stats.GetStat(StatType.AttackCooldown) * 0.9f;
                float total = outDur + backDur;
                if (budget > 0.01f && total > budget)
                {
                    float scale = budget / total;
                    outDur *= scale;
                    backDur *= scale;
                }
            }

            // Plain coroutine, no external dependency.
            // DOTween drop-in if the project adopts it later:
            //   transform.DOLocalMove(_restLocalPos + localPunch, outDur).SetEase(Ease.OutQuad)
            //       .OnComplete(() => transform.DOLocalMove(_restLocalPos, backDur).SetEase(Ease.OutSine));
            for (float t = 0f; t < outDur; t += Time.deltaTime)
            {
                float k = 1f - (1f - t / outDur) * (1f - t / outDur); // ease-out quad
                transform.localPosition = _restLocalPos + localPunch * k;
                yield return null;
            }
            for (float t = 0f; t < backDur; t += Time.deltaTime)
            {
                transform.localPosition = Vector3.Lerp(
                    _restLocalPos + localPunch, _restLocalPos, Mathf.Sin(t / backDur * Mathf.PI * 0.5f));
                yield return null;
            }

            transform.localPosition = _restLocalPos;
            _recoilRoutine = null;
        }
    }
}
