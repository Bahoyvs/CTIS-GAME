using UnityEngine;

namespace CBuilding.Utilities
{
    /// <summary>
    /// Screen-space facing directions. N = character walking "up" the screen
    /// (away from the camera), S = toward the camera. Indices 0-7 clockwise.
    /// </summary>
    public enum FacingDirection8
    {
        N = 0, NE = 1, E = 2, SE = 3, S = 4, SW = 5, W = 6, NW = 7
    }

    /// <summary>
    /// Maps a world-space facing vector to one of 8 sprite states, correctly compensating
    /// for the isometric camera's 45° Y rotation.
    ///
    /// THE MATH:
    /// Sprites are authored in SCREEN space (an "E" sprite walks screen-right), but aiming
    /// happens in WORLD space. Because the camera is yawed 45°, world +Z is NOT screen-up —
    /// screen-up is the camera's forward vector flattened onto the ground plane.
    /// So instead of measuring the facing angle against world +Z, we measure the SIGNED
    /// angle between the flattened camera forward and the facing vector, around the Y axis:
    ///
    ///     angle = SignedAngle(camForwardFlat, facing, up)   // in [-180, 180]
    ///        0  => facing away from camera => N
    ///      +90  => camera-right            => E
    ///     ±180  => toward camera           => S
    ///      -90  => camera-left             => W
    ///
    /// Sector index = RoundToInt(angle / 45) wrapped to [0, 7]. Rounding centers each 45°
    /// sector on its direction (N covers -22.5°..+22.5°, etc.). If the camera rig ever
    /// rotates (e.g. rotating rooms), this keeps working with zero changes.
    ///
    /// Drives EITHER an Animator int parameter ("Direction", one blend/state per facing)
    /// OR a raw Sprite[8] array — assign whichever workflow you're using.
    /// </summary>
    public class IsometricSprite8Dir : MonoBehaviour
    {
        private static readonly int DirectionParam = Animator.StringToHash("Direction");

        [Header("Scene References")]
        [Tooltip("Camera used for the angle basis. Defaults to Camera.main.")]
        [SerializeField] private Camera targetCamera;

        [Header("Output Mode (assign one)")]
        [Tooltip("If set, writes the 0-7 index to the int parameter \"Direction\".")]
        [SerializeField] private Animator animator;
        [Tooltip("Fallback: directly swaps sprites. Order: N, NE, E, SE, S, SW, W, NW.")]
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Sprite[] directionSprites = new Sprite[8];

        public FacingDirection8 CurrentDirection { get; private set; } = FacingDirection8.S;

        private Transform _camTransform;
        private bool _initialized;

        private void Awake()
        {
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera != null) _camTransform = targetCamera.transform;
        }

        /// <summary>
        /// Call every frame with the character's world-space facing (mouse aim for the hero,
        /// velocity / target direction for enemies).
        /// </summary>
        public void SetFacing(Vector3 worldFacing)
        {
            worldFacing.y = 0f;
            if (_camTransform == null || worldFacing.sqrMagnitude < 0.0001f) return;

            // Flatten camera forward onto the ground plane — this is "screen up" in world space.
            Vector3 camForward = _camTransform.forward;
            camForward.y = 0f;

            float angle = Vector3.SignedAngle(camForward, worldFacing, Vector3.up); // [-180, 180]

            // -180..180 -> sector -4..4 -> wrap to 0..7 (both -4 and 4 mean S).
            int index = Mathf.RoundToInt(angle / 45f);
            index = (index + 8) % 8;

            Apply((FacingDirection8)index);
        }

        /// <summary>
        /// Directly set a facing state, bypassing the angle math. Used on REMOTE proxies in
        /// multiplayer: the owner computes the sector locally and replicates only the 1-byte
        /// index, so remote machines never need the aim vector at all.
        /// </summary>
        public void SetDirection(FacingDirection8 direction) => Apply(direction);

        private void Apply(FacingDirection8 direction)
        {
            // Skip redundant Animator/renderer writes — but always apply the very first call.
            if (_initialized && direction == CurrentDirection) return;
            _initialized = true;
            CurrentDirection = direction;

            if (animator != null)
                animator.SetInteger(DirectionParam, (int)direction);
            else if (spriteRenderer != null && directionSprites.Length == 8)
                spriteRenderer.sprite = directionSprites[(int)direction];
        }
    }
}
