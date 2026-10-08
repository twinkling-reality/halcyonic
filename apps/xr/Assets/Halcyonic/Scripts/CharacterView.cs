#nullable enable
using Halcyonic.Client;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR
{
    /// <summary>
    /// A workstream's character: a glossy bot whose shape and color are the workstream's identity
    /// and whose eyes, motion and light show its state
    /// (docs/internal/decisions/0013-characters-are-bots-with-a-living-surface.md), with its label
    /// underneath: the state badge, the task's title and its mark, kept apart
    /// (<see cref="CharacterLabelView"/>, ADR 0023), so no state depends on color or motion alone.
    /// </summary>
    /// <remarks>
    /// The character's own transform is its place on the stage: its forward axis points at the
    /// person, and the stage scales it with its distance, so the sizes here are meters as seen from
    /// one meter away. Only <see cref="Body"/> moves; the labels stay where they are. The cues come
    /// from <see cref="CharacterCues"/>; this class only chooses shapes, timings and colors, and it
    /// allocates nothing per frame.
    /// </remarks>
    public sealed class CharacterView : MonoBehaviour
    {
        /// <summary>
        /// The radius of a sphere around <see cref="Body"/>'s origin that holds the body in every
        /// state, in Body's local units.
        /// </summary>
        public const float BodyRadius = 0.1f;

        /// <summary>
        /// How far the body reaches around its center at any moment, in Body's local units: its
        /// radius, squashed or stretched. <see cref="BodyRadius"/> adds room for the body moving.
        /// </summary>
        public const float BodyExtent = 0.075f;

        /// <summary>
        /// The body mesh is about one unit in radius; this makes it 7 cm at the one-meter scale, about
        /// 8 degrees across.
        /// </summary>
        private const float BodyScale = 0.07f;

        /// <summary>How far a character that needs its person rises, at most.</summary>
        private const float RiseHeight = 0.085f;

        /// <summary>The highest a character's body reaches above its place, risen and moving, in its own units.</summary>
        public const float HighestReach = RiseHeight + BodyRadius;

        // The lifts the settled, slumped and frozen motions hold, in the character's units.
        private const float SettledLift = -0.0032f;
        private const float SlumpedLift = -0.0095f;
        private const float FrozenLift = 0.0056f;

        private const float EyeSwitchSeconds = 0.18f;

        private static readonly int BodyColorId = Shader.PropertyToID("_BodyColor");
        private static readonly int ClockId = Shader.PropertyToID("_Clock");
        private static readonly int FlowId = Shader.PropertyToID("_Flow");
        private static readonly int FlowSpeedId = Shader.PropertyToID("_FlowSpeed");
        private static readonly int CrackId = Shader.PropertyToID("_Crack");
        private static readonly int FogId = Shader.PropertyToID("_Fog");
        private static readonly int SaturationId = Shader.PropertyToID("_Saturation");
        private static readonly int GhostId = Shader.PropertyToID("_Ghost");
        private static readonly int EyeKindId = Shader.PropertyToID("_EyeKind");
        private static readonly int EyeOpenId = Shader.PropertyToID("_EyeOpen");
        private static readonly int EyeUnevenId = Shader.PropertyToID("_EyeUneven");

        /// <summary>How much more one lid of a character that can't be told stands open than the other (ADR 0013: lids uneven).</summary>
        private const float UnsureUneven = 0.3f;

        /// <summary>Where a character that can't be told looks while things are kept still: aside and a little up, a still look around.</summary>
        private static readonly Vector2 UnsureStillLook = new Vector2(0.55f, 0.35f);
        private static readonly int EyeInkId = Shader.PropertyToID("_EyeInk");
        private static readonly int LookId = Shader.PropertyToID("_Look");
        private static readonly int EyeLayoutId = Shader.PropertyToID("_EyeLayout");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private static readonly Color NeedsYouLight = new Color(1f, 0.68f, 0.24f);
        private static readonly Color FailedLight = new Color(1f, 0.35f, 0.31f);
        private static readonly Color FinishedLight = new Color(0.31f, 0.82f, 0.54f);
        private static readonly Color VerifyingLight = new Color(0.84f, 0.95f, 1f);
        private static readonly Color UnknownLight = new Color(0.6f, 0.64f, 0.7f);
        private static readonly Color RingColor = new Color(0.85f, 0.96f, 1f);

        private Transform body = null!;
        private Transform shell = null!;
        private MeshRenderer shellRenderer = null!;
        private MeshRenderer haloRenderer = null!;
        private Transform ring = null!;
        private MeshRenderer ringRenderer = null!;
        private CharacterLabelView label = null!;
        private MaterialPropertyBlock bodyBlock = null!;
        private MaterialPropertyBlock haloBlock = null!;
        private MaterialPropertyBlock ringBlock = null!;
        private CharacterIdentity identity = null!;
        private Vector4 bodyColor;
        private Vector4 eyeLayout;

        private CharacterPresentation? presentation;
        private CharacterCues? cues;
        private bool lookAtPerson;

        // The animation, advanced every frame toward the cues' targets.
        private float clock;

        /// <summary>The clock its surface's flow and fog and its sweep ring run on: it stands while things are kept still.</summary>
        private float surfaceClock;
        private float lift;
        private float squash;
        private float eyeOpen = 1f;
        private float lookX;
        private float lookY;
        private float eyeInk = 1f;
        private float eyeUneven;
        private float eyeKind;
        private float nextEyeKind;
        private float eyeSwitch;
        private float flow;
        private float flowSpeed = 0.55f;
        private float crack;
        private float fog;
        private float saturation = 1f;
        private float ghost;
        private float haloStrength;
        private Color haloColor = Color.black;
        private float ringAlpha;

        /// <summary>
        /// The character's visual root: it hops, rises, slumps and turns with the character's state.
        /// Its scale stays one, the body is centered on its origin within <see cref="BodyRadius"/>,
        /// and its forward axis is the direction the character faces.
        /// </summary>
        public Transform Body => body;

        public string WorkstreamId { get; private set; } = "";

        /// <summary>What the character shows now, or null before the first <see cref="Show"/>.</summary>
        public CharacterPresentation? Presentation => presentation;

        /// <summary>The person's head, which a character that needs them turns to. Set by the stage.</summary>
        internal Transform? Person { get; set; }

        /// <summary>
        /// The lowest point of the character at rest, the bottom of its label, mark included, in its
        /// own units below its origin: what rests on a surface.
        /// </summary>
        internal float Footing => label.Bottom;

        /// <summary>Half the width of the character's label, in its own units: how far to each side it reaches.</summary>
        public float LabelHalfWidth => label.HalfWidth;

        /// <summary>How far below the character's origin its label reaches, in its own units: a negative height.</summary>
        public float LabelBottom => label.Bottom;

        /// <summary>The label under the character, for renders and their checks.</summary>
        public CharacterLabelView Label => label;

        /// <summary>Shows only the label's badge and marks, as the characters beside a window do (<see cref="CharacterLabelView.BadgeOnly"/>).</summary>
        public bool BadgeOnly
        {
            get => label.BadgeOnly;
            set => label.BadgeOnly = value;
        }

        /// <summary>
        /// How far below or above eye level the person sees the character, in degrees, which sets how
        /// low its label hangs, and whether its label leans back to face the eyes, as on a desk.
        /// </summary>
        public void ViewedFrom(float elevationDegrees, bool faceEyes = false) => label.ViewFrom(elevationDegrees, faceEyes);

        public static CharacterView Create(Transform parent, string workstreamId)
        {
            var root = new GameObject("Character " + workstreamId);
            root.transform.SetParent(parent, false);
            var view = root.AddComponent<CharacterView>();
            view.Build(workstreamId);
            return view;
        }

        public void Show(CharacterPresentation next)
        {
            var first = presentation == null;
            presentation = next;
            cues = CharacterCues.Of(next);
            var kind = EyeKindOf(cues.Eyes);
            if (first)
            {
                eyeKind = kind;
                nextEyeKind = kind;
            }
            else if (kind != nextEyeKind)
            {
                // A change of eyes happens through a blink.
                nextEyeKind = kind;
                eyeSwitch = EyeSwitchSeconds;
            }
            label.Show(CharacterLabel.Of(next));
            if (first) Advance(0f, true);
        }

#if UNITY_EDITOR
        /// <summary>For the editor's renders, which run no frames: one frame of <paramref name="seconds"/>, as the headset steps it.</summary>
        public void AdvanceForRender(float seconds) => Advance(seconds, false);

        /// <summary>For the editor's renders: how its eyes stand and how strong its light is now.</summary>
        public (float Open, float LookX, float LookY, float Ink, float Halo, float Uneven) PoseForRender => (eyeOpen, lookX, lookY, eyeInk, haloStrength, eyeUneven);
#endif

        /// <summary>
        /// While <paramref name="look"/> is true, the character turns to face the person and focuses
        /// its open eyes on them: for the workspace, while the character is hovered or opened. The
        /// state's own cues stay, so closed, crossed or flat eyes stay as they are. The latest call
        /// wins.
        /// </summary>
        public void LookAtPerson(bool look) => lookAtPerson = look;

        private void Build(string workstreamId)
        {
            WorkstreamId = workstreamId;
            CharacterMaterials.Prepare();
            identity = CharacterIdentity.Of(workstreamId);
            var color = Color.HSVToRGB((float)identity.Hue / 360f, (float)identity.Saturation, (float)identity.Value);
            // The shader lights in sRGB, as the lookbook did, and converts at the end.
            bodyColor = new Vector4(color.r, color.g, color.b, 1f);
            eyeLayout = CharacterMeshes.EyeLayout(identity.Shape);
            clock = (float)identity.Phase * 60f;
            surfaceClock = clock;

            body = new GameObject("Body").transform;
            body.SetParent(transform, false);

            shellRenderer = CreateRenderer(body, "Shell", CharacterMeshes.Body(identity.Shape), CharacterMaterials.Body);
            shell = shellRenderer.transform;
            shell.localScale = Vector3.one * BodyScale;

            haloRenderer = CreateRenderer(body, "Halo", CharacterMeshes.Quad(), CharacterMaterials.Halo);
            haloRenderer.transform.localPosition = new Vector3(0f, 0f, -0.3f * BodyScale);
            haloRenderer.transform.localScale = Vector3.one * (4.4f * BodyScale);
            haloRenderer.enabled = false;

            ringRenderer = CreateRenderer(body, "Ring", CharacterMeshes.Band(), CharacterMaterials.Ring);
            ring = ringRenderer.transform;
            ring.localScale = Vector3.one * BodyScale;
            ringRenderer.enabled = false;

            // Text reads along its parent's forward axis, and the character faces the person.
            var labels = new GameObject("Labels").transform;
            labels.SetParent(transform, false);
            labels.localRotation = Quaternion.Euler(0f, 180f, 0f);
            label = CharacterLabelView.Create(labels);

            bodyBlock = new MaterialPropertyBlock();
            haloBlock = new MaterialPropertyBlock();
            ringBlock = new MaterialPropertyBlock();
        }

        private static MeshRenderer CreateRenderer(Transform parent, string name, Mesh mesh, Material material)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return renderer;
        }

        private void Update() => Advance(Time.deltaTime, false);

        /// <summary>Moves the animation on by a frame, or straight to the state's pose when snapping.</summary>
        private void Advance(float deltaTime, bool snap)
        {
            if (cues == null) return;
            var still = GlazeMotion.Still;
            if (!cues.Paused) clock += deltaTime;
            // Kept still (ADR 0027), the surface's clock stands, so its flow, its fog and its sweep ring stand where they are.
            if (!cues.Paused && !still) surfaceClock += deltaTime;
            var t = clock;

            // The body's motion.
            var targetLift = 0f;
            var targetSquash = 0f;
            var yaw = 0f;
            var roll = 0f;
            switch (cues.Motion)
            {
                case CharacterMotion.Breathe:
                    targetLift = Mathf.Sin(t * 1.3f) * 0.003f;
                    targetSquash = Mathf.Sin(t * 1.3f) * 0.012f;
                    break;
                case CharacterMotion.Warm:
                    targetLift = Mathf.Sin(t * 2.4f) * 0.004f;
                    targetSquash = Mathf.Sin(t * 4.8f) * 0.01f;
                    yaw = Mathf.Sin(t * 0.9f) * 0.08f;
                    break;
                case CharacterMotion.Hop:
                {
                    var hop = Mathf.Abs(Mathf.Sin(t * 3.1f));
                    targetLift = hop * 0.0145f;
                    targetSquash = hop < 0.22f ? (0.22f - hop) * 0.35f : 0f;
                    yaw = Mathf.Sin(t * 0.6f) * 0.18f;
                    break;
                }
                case CharacterMotion.Hover:
                    targetLift = Mathf.Sin(t * 1.4f) * 0.0032f;
                    break;
                case CharacterMotion.Rise:
                    targetLift = RiseRoom() + Mathf.Sin(t * 2.1f) * 0.0025f;
                    targetSquash = -Mathf.Max(0f, Mathf.Sin(t * 4.2f)) * 0.025f;
                    break;
                case CharacterMotion.Settle:
                    targetLift = SettledLift + Mathf.Sin(t * 1.1f) * 0.001f;
                    targetSquash = 0.02f + Mathf.Sin(t * 1.1f) * 0.01f;
                    break;
                case CharacterMotion.Slump:
                    targetLift = SlumpedLift;
                    targetSquash = 0.07f;
                    roll = 0.14f;
                    break;
                case CharacterMotion.Drift:
                    targetLift = Mathf.Sin(t * 0.7f) * 0.002f;
                    yaw = Mathf.Sin(t * 0.31f) * 0.1f;
                    break;
                case CharacterMotion.Frozen:
                    // Stopped mid-hop, a little turned.
                    targetLift = FrozenLift;
                    targetSquash = -0.03f;
                    yaw = 0.12f;
                    roll = -0.05f;
                    break;
            }
            if (still)
            {
                // At its motion's rest pose: no hop, hover, drift, breath or bob; a slump or a stop keeps its own lean.
                targetLift = HeldLift;
                targetSquash = cues.Motion switch
                {
                    CharacterMotion.Settle => 0.02f,
                    CharacterMotion.Slump => 0.07f,
                    CharacterMotion.Frozen => -0.03f,
                    _ => 0f,
                };
                if (cues.Motion != CharacterMotion.Slump && cues.Motion != CharacterMotion.Frozen)
                {
                    yaw = 0f;
                    roll = 0f;
                }
            }

            // The eyes.
            var targetOpen = 1f;
            var targetLookX = 0f;
            var targetLookY = 0f;
            var targetInk = 1f;
            var targetUneven = 0f;
            var openEyes = true;
            switch (cues.Eyes)
            {
                case CharacterEyes.Open:
                    targetLookX = Mathf.Sin(t * 0.23f) * 0.35f;
                    targetLookY = Mathf.Sin(t * 0.17f) * 0.2f;
                    break;
                case CharacterEyes.OnTask:
                    targetOpen = 0.78f;
                    targetLookX = Mathf.Sin(t * 0.8f) * 0.7f;
                    targetLookY = -0.7f;
                    break;
                case CharacterEyes.Scanning:
                    targetOpen = 0.9f;
                    targetLookX = Mathf.Sin(t * 2.6f);
                    targetLookY = -0.15f;
                    break;
                case CharacterEyes.OnPerson:
                    targetOpen = 1.12f;
                    break;
                case CharacterEyes.Unfocused:
                    targetOpen = 0.42f;
                    targetUneven = UnsureUneven;
                    targetLookX = Mathf.Sin(t * 0.37f) * 0.9f;
                    targetLookY = Mathf.Cos(t * 0.29f) * 0.6f;
                    targetInk = 0.75f;
                    break;
                case CharacterEyes.Closed:
                    openEyes = false;
                    targetLookY = -0.25f;
                    break;
                case CharacterEyes.Crossed:
                    openEyes = false;
                    targetLookY = -0.15f;
                    break;
                case CharacterEyes.Flat:
                    openEyes = false;
                    targetLookX = 0.25f;
                    break;
            }
            if (lookAtPerson && openEyes && cues.Eyes != CharacterEyes.Unfocused)
            {
                targetLookX = 0f;
                targetLookY = 0f;
            }
            if (still)
            {
                // Eyes that scan stand at their middle, and a working character's still look down. One that can't be
                // told holds a still look aside and a little up, its lids uneven, so it never reads as a sleepy Working.
                if (cues.Eyes == CharacterEyes.Open || cues.Eyes == CharacterEyes.OnTask || cues.Eyes == CharacterEyes.Scanning) targetLookX = 0f;
                if (cues.Eyes == CharacterEyes.Open) targetLookY = 0f;
                if (cues.Eyes == CharacterEyes.Unfocused)
                {
                    targetLookX = UnsureStillLook.x;
                    targetLookY = UnsureStillLook.y;
                }
            }
            if (openEyes && !still)
            {
                var blinkPeriod = 3.4f + (float)identity.Phase * 1.9f;
                if (t % blinkPeriod < 0.13f) targetOpen *= 0.1f;
            }
            if (eyeSwitch > 0f)
            {
                eyeSwitch = Mathf.Max(0f, eyeSwitch - deltaTime);
                if (eyeSwitch <= EyeSwitchSeconds / 2f) eyeKind = nextEyeKind;
                targetOpen *= Mathf.Max(0.08f, Mathf.Abs(eyeSwitch / EyeSwitchSeconds * 2f - 1f));
            }
            if (cues.Ghosted) targetInk *= 0.85f;

            // The surface.
            var targetFlow = cues.Flowing ? 1f : 0f;
            var targetFlowSpeed = cues.Motion == CharacterMotion.Hover ? 0.95f : 0.55f;
            var targetCrack = cues.Cracked ? 1f : 0f;
            var targetFog = cues.Fogged ? 1f : 0f;
            var targetGhost = cues.Ghosted ? 1f : 0f;
            var targetSaturation = cues.Ghosted ? 0.18f
                : cues.Motion == CharacterMotion.Frozen ? 0.55f
                : cues.Cracked ? 0.72f
                : cues.Fogged ? 0.45f
                : 1f;

            // The light around it.
            var targetHaloColor = haloColor;
            var targetHalo = 0f;
            switch (cues.Halo)
            {
                case CharacterHalo.NeedsYou:
                    targetHaloColor = NeedsYouLight;
                    // Its pulse stands still when kept still, at the middle of its breath.
                    targetHalo = 0.75f * (0.8f + (still ? 0f : 0.25f * Mathf.Sin(t * 3.2f)));
                    break;
                case CharacterHalo.Failed:
                    targetHaloColor = FailedLight;
                    targetHalo = 0.5f;
                    break;
                case CharacterHalo.Finished:
                    targetHaloColor = FinishedLight;
                    targetHalo = 0.3f;
                    break;
                case CharacterHalo.Verifying:
                    targetHaloColor = VerifyingLight;
                    targetHalo = 0.28f;
                    break;
                case CharacterHalo.Unknown:
                    targetHaloColor = UnknownLight;
                    targetHalo = 0.28f;
                    break;
            }
            if (cues.Ghosted) targetHalo *= 0.3f;
            var targetRing = cues.Ring ? 1f : 0f;

            var ease = snap ? 1f : 1f - Mathf.Exp(-deltaTime * 8f);
            var quick = snap ? 1f : 1f - Mathf.Exp(-deltaTime * 30f);
            lift = Mathf.Lerp(lift, targetLift, ease);
            squash = Mathf.Lerp(squash, targetSquash, ease);
            eyeOpen = Mathf.Lerp(eyeOpen, targetOpen, quick);
            lookX = Mathf.Lerp(lookX, targetLookX, quick);
            lookY = Mathf.Lerp(lookY, targetLookY, quick);
            eyeInk = Mathf.Lerp(eyeInk, targetInk, ease);
            eyeUneven = Mathf.Lerp(eyeUneven, targetUneven, ease);
            flow = Mathf.Lerp(flow, targetFlow, ease);
            flowSpeed = Mathf.Lerp(flowSpeed, targetFlowSpeed, ease);
            crack = Mathf.Lerp(crack, targetCrack, ease);
            fog = Mathf.Lerp(fog, targetFog, ease);
            saturation = Mathf.Lerp(saturation, targetSaturation, ease);
            ghost = Mathf.Lerp(ghost, targetGhost, ease);
            haloColor = snap || haloStrength < 0.01f ? targetHaloColor : Color.Lerp(haloColor, targetHaloColor, ease);
            haloStrength = Mathf.Lerp(haloStrength, targetHalo, ease);
            ringAlpha = Mathf.Lerp(ringAlpha, targetRing, ease);
            if (snap) eyeKind = nextEyeKind;

            Turn(yaw, roll, snap ? 1f : 1f - Mathf.Exp(-deltaTime * 5f));
            Apply(surfaceClock);
        }

        /// <summary>
        /// The lift the body's motion holds, in the character's units: risen while it waits, settled or
        /// slumped when done or stopped, without the bob, hop or breath its motion adds round it. Where
        /// placement takes the body to stand (<see cref="CharacterTarget.RestPosition"/>).
        /// </summary>
        public float HeldLift => cues == null ? 0f : cues.Motion switch
        {
            CharacterMotion.Rise => RiseRoom(),
            CharacterMotion.Settle => SettledLift,
            CharacterMotion.Slump => SlumpedLift,
            CharacterMotion.Frozen => FrozenLift,
            _ => 0f,
        };

        /// <summary>
        /// How far a character that needs its person can rise without passing their eye level, in
        /// the character's own units.
        /// </summary>
        private float RiseRoom()
        {
            if (Person == null) return RiseHeight;
            var scale = Mathf.Max(transform.lossyScale.y, 0.01f);
            var room = (Person.position.y - transform.position.y) / scale - 0.02f;
            return Mathf.Clamp(room, 0f, RiseHeight);
        }

        private void Turn(float yaw, float roll, float ease)
        {
            var facing = cues!.FacesPerson || lookAtPerson;
            if (facing && cues.Paused) return;
            // Relative to the character's place, so the body moves with the stage when it is placed again.
            Quaternion target;
            if (facing && Person != null)
            {
                var toPerson = Person.position - body.position;
                var level = new Vector3(toPerson.x, 0f, toPerson.z);
                if (level.sqrMagnitude < 1e-6f) return;
                // Tip up or down toward the person's eyes, but not so far that the face turns away.
                var pitch = Mathf.Clamp(Mathf.Atan2(toPerson.y, level.magnitude) * Mathf.Rad2Deg, -30f, 30f);
                var world = Quaternion.LookRotation(level, Vector3.up) * Quaternion.Euler(-pitch, 0f, roll * Mathf.Rad2Deg);
                target = Quaternion.Inverse(transform.rotation) * world;
            }
            else
            {
                target = Quaternion.Euler(0f, yaw * Mathf.Rad2Deg, roll * Mathf.Rad2Deg);
            }
            body.localRotation = Quaternion.Slerp(body.localRotation, target, ease);
        }

        private void Apply(float t)
        {
            body.localPosition = new Vector3(0f, lift, 0f);
            shell.localScale = new Vector3(BodyScale * (1f + squash), BodyScale * (1f - squash), BodyScale * (1f + squash));

            bodyBlock.SetVector(BodyColorId, bodyColor);
            bodyBlock.SetFloat(ClockId, t);
            bodyBlock.SetFloat(FlowId, flow);
            bodyBlock.SetFloat(FlowSpeedId, flowSpeed);
            bodyBlock.SetFloat(CrackId, crack);
            bodyBlock.SetFloat(FogId, fog);
            bodyBlock.SetFloat(SaturationId, saturation);
            bodyBlock.SetFloat(GhostId, ghost);
            bodyBlock.SetFloat(EyeKindId, eyeKind);
            bodyBlock.SetFloat(EyeOpenId, eyeOpen);
            bodyBlock.SetFloat(EyeUnevenId, eyeUneven);
            bodyBlock.SetFloat(EyeInkId, eyeInk);
            bodyBlock.SetVector(LookId, new Vector4(lookX, lookY, 0f, 0f));
            bodyBlock.SetVector(EyeLayoutId, eyeLayout);
            shellRenderer.SetPropertyBlock(bodyBlock);

            haloRenderer.enabled = haloStrength > 0.004f;
            if (haloRenderer.enabled)
            {
                haloBlock.SetColor(ColorId, new Color(haloColor.r, haloColor.g, haloColor.b, haloStrength));
                haloRenderer.SetPropertyBlock(haloBlock);
            }

            ringRenderer.enabled = ringAlpha > 0.004f;
            if (ringRenderer.enabled)
            {
                // Level around the body, tipping back and forth, and sweeping around.
                ring.localRotation = Quaternion.Euler(90f + 25f * Mathf.Sin(t * 1.1f), 0f, 0f) * Quaternion.Euler(0f, 0f, t * 92f);
                ringBlock.SetColor(ColorId, new Color(RingColor.r, RingColor.g, RingColor.b, 0.9f * ringAlpha));
                ringRenderer.SetPropertyBlock(ringBlock);
            }
        }

        private static float EyeKindOf(CharacterEyes eyes)
        {
            switch (eyes)
            {
                case CharacterEyes.Closed:
                    return 1f;
                case CharacterEyes.Crossed:
                    return 2f;
                case CharacterEyes.Flat:
                    return 3f;
                default:
                    return 0f;
            }
        }
    }
}
