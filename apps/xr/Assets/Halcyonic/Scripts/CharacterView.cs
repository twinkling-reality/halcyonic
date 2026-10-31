#nullable enable
using Halcyonic.Client;
using Halcyonic.XR.UI;
using UnityEngine;
using TMPro;

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
    /// one meter away. Only <see cref="Body"/> moves; the labels stay where they are. Its look comes
    /// from <see cref="CharacterCues"/>; its life (floats, blinks, gaze, accents, the hop at the
    /// computer's own passed check, arrival and Put Away) from <see cref="CharacterLife"/>, shown
    /// through its figure (<see cref="ICharacterFigure"/>), today's body. Last Known holds every
    /// value. It allocates nothing per frame.
    /// </remarks>
    public sealed class CharacterView : MonoBehaviour
    {
        /// <summary>
        /// The radius of a sphere around <see cref="Body"/>'s origin that holds the body in every
        /// state, its feature included, in Body's local units.
        /// </summary>
        public const float BodyRadius = GlazeTokens.CharacterBodyRadius;

        /// <summary>
        /// How far the visible body and feature reach around their center at any moment, in Body's
        /// local units, squashed or stretched. Placement clears this whole envelope.
        /// </summary>
        public const float BodyExtent = BodyRadius;

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
        private const float SlumpedLift = GlazeTokens.CharacterSlumpedLift;
        private const float FrozenLift = 0.0056f;

        private const float EyeSwitchSeconds = 0.18f;

        /// <summary>Where it looks for its screen, its work, which it has no point for here: ahead and down, in degrees.</summary>
        private const float ScreenDownDegrees = 25f;

        /// <summary>A look away: aside and down; a look at a neighbour it has no point for: aside, level. Degrees.</summary>
        private const float AwayAsideDegrees = 25f, AwayDownDegrees = 12f, NeighbourAsideDegrees = 30f;

        /// <summary>The person's lap, below their eyes, in meters.</summary>
        private const float LapBelowEyes = 0.55f;

        /// <summary>How far today's body turns to look, aside and up or down, in degrees: about as far as it ever turned.</summary>
        private static readonly Vector2 TurnReach = new Vector2(20f, 10f);

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

        /// <summary>The feature on top of its body, moving and squashing with it; null for none (ADR 0013).</summary>
        private MeshRenderer? featureRenderer;

        /// <summary>The feature's colour: the body's, lightened, never a state's.</summary>
        private Vector4 featureColor;

        /// <summary>How far toward white a feature's colour is from its body's.</summary>
        private const float FeatureLighten = 0.35f;
        private MeshRenderer haloRenderer = null!;
        private Transform ring = null!;
        private MeshRenderer ringRenderer = null!;
        private CharacterLabelView label = null!;
        private Transform labels = null!;
        private TMP_Text[]? shelfWords;
        private MeshFilter[]? shelfBadgeMeshes;
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

        /// <summary>The life it shows, its figure, and where it looks now, followed.</summary>
        private CharacterLife life = null!;
        private ICharacterFigure figure = null!;
        private float aimYaw, aimPitch, aimEyesX, aimEyesY;
        private Vector3 homeOffset;

        /// <summary>The posture its cues hold, eased, under the life's float and hop.</summary>
        private float restLift, restSquash;

        /// <summary>The word its tag says over its state's, while it is put away.</summary>
        private string? saying;

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
        /// Its scale stays one, the body and feature stay within <see cref="BodyRadius"/> of its origin,
        /// and its forward axis is the direction the character faces.
        /// </summary>
        public Transform Body => body;
        public bool Grounded { get; set; }
        private Vector3[] shellVertices = System.Array.Empty<Vector3>();

        public string WorkstreamId { get; private set; } = "";

        private bool bodyOnlyPresentation;
        /// <summary>A local task portrait shares this real identity and state, with the surrounding
        /// task heading supplying words. It owns no stage placement or interaction target.</summary>
        public bool BodyOnlyPresentation
        {
            get => bodyOnlyPresentation;
            set
            {
                bodyOnlyPresentation = value;
                if (labels != null) labels.gameObject.SetActive(!value);
                if (value) { haloRenderer.enabled = false; ringRenderer.enabled = false; }
            }
        }

        /// <summary>What the character shows now, or null before the first <see cref="Show"/>.</summary>
        public CharacterPresentation? Presentation => presentation;

        /// <summary>The person's head, which a character that needs them turns to. Set by the stage.</summary>
        internal Transform? Person { get; set; }

        /// <summary>
        /// The lowest point of the character at rest, the bottom of its label, mark included, in its
        /// own units below its origin, used by legacy label-resting gallery geometry.
        /// Grounded shelves use the animated shell mesh contact instead.
        /// </summary>
        internal float Footing => label.Bottom;

        /// <summary>Half the width of the character's label, in its own units: how far to each side it reaches.</summary>
        public float LabelHalfWidth => label.HalfWidth * labels.localScale.x;

        /// <summary>How far below the character's origin its label reaches, in its own units: a negative height.</summary>
        public float LabelBottom => transform.InverseTransformPoint(label.transform.TransformPoint(new Vector3(0f, label.Bottom, 0f))).y;

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

        /// <summary>Places readable rendered labels independently of the fixed grounded body's scale.
        /// The placement-time viewer is retained; later head motion does not move the shelf or label.</summary>
        public void PlaceShelfLabel(Vector3 viewer)
        {
            // The character faces the viewer; its forward edge is the actual shelf's front lip.
            // Only the label moves. The grounded shell and confirmed shelf pose remain untouched.
            var lip = transform.position + transform.forward * (AgentShelfDraft.Depth / 2f);
            labels.position = lip;
            var away = lip - viewer;
            var horizontal = new Vector3(away.x, 0f, away.z);
            if (horizontal.sqrMagnitude < 1e-6f) return;
            labels.rotation = Quaternion.LookRotation(horizontal, Vector3.up);
            labels.localScale = Vector3.one * (away.magnitude / Mathf.Max(transform.lossyScale.x, 0.001f));
            ViewedFrom(Mathf.Atan2(away.y, horizontal.magnitude) * Mathf.Rad2Deg, faceEyes: true);
            var words = shelfWords ??= label.GetComponentsInChildren<TMP_Text>(true);
            foreach (var word in words) word.ForceMeshUpdate(ignoreActiveState: true);
            var badgeMeshes = shelfBadgeMeshes ??= label.Badge.GetComponentsInChildren<MeshFilter>(true);
            void AttachTop()
            {
                var top = float.NegativeInfinity;
                var edgeMinimum = Vector3.zero;
                var edgeMaximum = Vector3.zero;
                foreach (var filter in badgeMeshes)
                {
                    if (!filter.gameObject.activeInHierarchy || filter.sharedMesh == null) continue;
                    var bounds = filter.sharedMesh.bounds;
                    for (var corner = 0; corner < 8; corner++)
                    {
                        var point = bounds.center + Vector3.Scale(bounds.extents,
                            new Vector3(corner % 2 == 0 ? -1f : 1f, corner < 4 ? -1f : 1f, corner % 4 < 2 ? -1f : 1f));
                        var world = filter.transform.TransformPoint(point);
                        if (world.y > top + 0.000001f)
                        {
                            top = world.y; edgeMinimum = edgeMaximum = world;
                        }
                        else if (Mathf.Abs(world.y - top) <= 0.000001f)
                        {
                            top = Mathf.Max(top, world.y);
                            edgeMinimum = Vector3.Min(edgeMinimum, world); edgeMaximum = Vector3.Max(edgeMaximum, world);
                        }
                    }
                }
                if (!float.IsNegativeInfinity(top))
                {
                    var middle = (edgeMinimum + edgeMaximum) / 2f; middle.y = top;
                    labels.position += lip - middle;
                }
            }
            AttachTop();
            // Measure the actual transformed em, including the mark's lower distance and tilt.
            // Scaling the entire label preserves its type hierarchy and provenance treatment.
            for (var pass = 0; pass < 3; pass++)
            {
                var grow = 1f;
                foreach (var word in words)
                {
                    if (!word.isActiveAndEnabled || string.IsNullOrEmpty(word.text) || GlazeIcons.IsIcon(word)) continue;
                    var middle = word.transform.TransformPoint(word.textBounds.center);
                    var half = word.transform.up * (word.fontSize * 0.1f * word.transform.lossyScale.y / 2f);
                    var degrees = Vector3.Angle(middle + half - viewer, middle - half - viewer);
                    if (degrees > 0.001f) grow = Mathf.Max(grow, (Glaze.MinimumTextDegrees + 0.015f) / degrees);
                }
                if (grow <= 1f) break;
                labels.localScale *= grow;
                AttachTop();
            }
        }

        public static CharacterView Create(Transform parent, string workstreamId)
        {
            var root = new GameObject("Character " + workstreamId);
            root.transform.SetParent(parent, false);
            var view = root.AddComponent<CharacterView>();
            view.Build(workstreamId);
            return view;
        }

        public void Show(CharacterPresentation next) => Show(next, CharacterCues.Of(next), false);

        /// <summary>
        /// Shows <paramref name="next"/> as it answers the moment its task shows in the room (ADR 0040, rule 4):
        /// <see cref="CharacterCues.Of(CharacterPresentation, KitMoment)"/>. A Ready answers only where it is the computer's own
        /// passed check observed in this round (<see cref="RoundStartedAt"/>, AUDIT B1); otherwise it answers nothing.
        /// </summary>
        public void Show(CharacterPresentation next, KitMoment? shown)
        {
            var passed = CharacterFacts.PassedCheckThisRound(shown, RoundStartedAt);
            Show(next, CharacterCues.Of(next, shown?.Kind == KitMomentKind.Ready && !passed ? null : shown), passed);
        }

        private void Show(CharacterPresentation next, CharacterCues nextCues, bool passedCheckThisRound)
        {
            var first = presentation == null;
            presentation = next;
            // Last Known only when the control plane is lost (AUDIT B2): the presentation is stale only then. First shown while
            // lost, it freezes in the state the journal last held, which its tag says as Last Known, never a state of its own.
            var state = StateLanguage.StateOf(next.Activity, next.Attention);
            if (first && next.Stale) life.Show(state, passedCheckThisRound, controlPlaneReachable: true);
            life.Show(state, passedCheckThisRound, controlPlaneReachable: !next.Stale);
            cues = nextCues;
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
            saying = life.TagWord;
            label.Show(saying == null ? CharacterLabel.Of(next) : CharacterLabel.Of(next).Saying(saying));
            shelfWords = null;
            shelfBadgeMeshes = null;
            if (first || cues.Paused)
            {
                // A paused state takes its appearance once, then holds every rendered value.
                eyeSwitch = 0f;
                Advance(0f, true);
            }
        }

#if UNITY_EDITOR
        /// <summary>For the editor's renders, which run no frames: one frame of <paramref name="seconds"/>, as the headset steps it.</summary>
        public void AdvanceForRender(float seconds) => Advance(seconds, false);

        /// <summary>The production animation clock for deterministic blink witnesses.</summary>
        public float ClockForRender => clock;
        public Vector4 BodyColourForRender => bodyColor;

        /// <summary>For the editor's renders: how its eyes stand and how strong its light is now.</summary>
        public (float Open, float LookX, float LookY, float Ink, float Halo, float Uneven) PoseForRender => (eyeOpen, lookX, lookY, eyeInk, haloStrength, eyeUneven);

        /// <summary>For the editor's renders: the eyes' drawn shape, 0 open, 1 closed, 2 crossed, 3 flat, as the shader takes it.</summary>
        public float EyeKindForRender => eyeKind;

        /// <summary>The feature's actual renderer and authored colour, for editor witnesses.</summary>
        public (MeshRenderer? Renderer, Color Colour) FeatureForRender => (featureRenderer, new Color(featureColor.x, featureColor.y, featureColor.z));
#endif

        /// <summary>
        /// When the round its task is in began, as the journal holds it, or null where nothing says: a Ready moment counts
        /// as this round's passed check only at or after it (AUDIT B1). No source sets it yet, so no passed look shows.
        /// </summary>
        public System.DateTimeOffset? RoundStartedAt { get; set; }

        /// <summary>Whether a placement gave it a home to wander in: without one it stays where it is placed.</summary>
        public bool HasHome { get; set; }

        /// <summary>The life it shows, for renders and their checks.</summary>
        public CharacterLife Life => life;

        /// <summary>A task starting while the person watches: it grows from a seed of light with its chime.</summary>
        public void Arrive() => life.Arrive();

        /// <summary>The person put its task away: "Put Away" on its tag first, then it folds; <see cref="Gone"/> after.</summary>
        public void PutAway() => life.PutAway();

        /// <summary>Put away, folded and its tag gone: the stage may remove it now, never before.</summary>
        public bool Gone => life.Gone;

        /// <summary>
        /// While <paramref name="look"/> is true, the character turns to face the person and focuses
        /// its open eyes on them: for the workspace, while the character is hovered or opened. The
        /// state's own cues stay, so closed, crossed or flat eyes stay as they are. The latest call
        /// wins.
        /// </summary>
        public void LookAtPerson(bool look) => lookAtPerson = look;

        /// <summary>Only this task body's local identity hue; geometry, clocks, eyes and state cues remain owned by their existing sources.</summary>
        public void SetIdentityHue(double? hue)
        {
            if (hue.HasValue && hue.Value != 197.0 && hue.Value != 285.0 && hue.Value != 329.0) throw new System.ArgumentException("Unsupported character colour.");
            var colour = Color.HSVToRGB((float)(hue ?? identity.Hue) / 360f, (float)identity.Saturation, (float)identity.Value);
            var next = new Vector4(colour.r, colour.g, colour.b, 1f);
            if (bodyColor == next) return;
            bodyColor = next;
            var lighter = Color.Lerp(colour, Color.white, FeatureLighten);
            featureColor = new Vector4(lighter.r, lighter.g, lighter.b, 1f);
            Apply(surfaceClock); // Redraw the held pose without advancing its animation or blink clock.
        }

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
            life = new CharacterLife(workstreamId, (KitFigureFeature)((int)identity.Feature % 6)) { SpotAllowed = (_, _) => HasHome };
            figure = new LegacyBody(this);

            body = new GameObject("Body").transform;
            body.SetParent(transform, false);

            shellRenderer = CreateRenderer(body, "Shell", CharacterMeshes.Body(identity.Shape), CharacterMaterials.Body);
            shell = shellRenderer.transform;
            shellVertices = shellRenderer.GetComponent<MeshFilter>().sharedMesh.vertices;
            shell.localScale = Vector3.one * BodyScale;

            if (CharacterMeshes.Feature(identity.Shape, identity.Feature) is Mesh featureMesh)
            {
                featureRenderer = CreateRenderer(shell, "Feature", featureMesh, CharacterMaterials.Feature);
                // The shell owns body depth; draw the non-depth-writing feature first at their junction.
                featureRenderer.sortingOrder = shellRenderer.sortingOrder - 1;
                var lighter = Color.Lerp(new Color(bodyColor.x, bodyColor.y, bodyColor.z), Color.white, FeatureLighten);
                featureColor = new Vector4(lighter.r, lighter.g, lighter.b, 1f);
            }

            haloRenderer = CreateRenderer(body, "Halo", CharacterMeshes.Quad(), CharacterMaterials.Halo);
            haloRenderer.transform.localPosition = new Vector3(0f, 0f, -0.3f * BodyScale);
            haloRenderer.transform.localScale = Vector3.one * (4.4f * BodyScale);
            haloRenderer.enabled = false;

            ringRenderer = CreateRenderer(body, "Ring", CharacterMeshes.Band(), CharacterMaterials.Ring);
            ring = ringRenderer.transform;
            ring.localScale = Vector3.one * BodyScale;
            ringRenderer.enabled = false;

            // Text reads along its parent's forward axis, and the character faces the person.
            labels = new GameObject("Labels").transform;
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

        /// <summary>
        /// Moves its life on by a frame and shows it, or goes straight to the state's pose when snapping. Last Known, the
        /// control plane lost, holds every rendered value: nothing moves, so a still character means no live news (AUDIT B2).
        /// </summary>
        private void Advance(float deltaTime, bool snap)
        {
            if (cues == null || presentation == null) return;
            if (presentation.Stale && !snap) return;
            var still = GlazeMotion.Still;
            life.KeepStill = still;
            var seconds = snap ? 0f : deltaTime;
            clock += seconds;
            // Kept still (ADR 0027), the surface's clock stands, so its flow, its fog and its sweep ring stand where they are.
            if (!still) surfaceClock += seconds;
            var pose = life.Step(seconds);
            figure.Show(pose, life.Look, Aim(pose, still, seconds, snap), seconds, snap);
            var sounds = life.TakeSounds();
            if (sounds.Count > 0) CharacterSoundPlayer.Play(sounds, life.Feature, figure.Head.position);
            ShowTag();
        }

        /// <summary>
        /// Where it looks, split between the head's turn and the eyes (<see cref="CharacterLife.Aim"/>) and followed, head 6 a
        /// second, eyes 14. Kept still, it drops the turn and its eyes rest at their middle (ADR 0027 item 6).
        /// </summary>
        private CharacterAim Aim(CharacterPose pose, bool still, float seconds, bool snap)
        {
            var (yaw, pitch) = still ? (0.0, 0.0) : LookAngles(lookAtPerson ? CharacterGaze.Person : pose.Gaze, pose.AvertSide);
            var (headYaw, headPitch, eyesX, eyesY) = CharacterLife.Aim(yaw, pitch);
            var dart = still ? 0 : pose.EyeDartH;
            var dt = snap || still ? 1000.0 : seconds;
            aimYaw = (float)CharacterLife.Damp(aimYaw, headYaw, 6, dt);
            aimPitch = (float)CharacterLife.Damp(aimPitch, headPitch, 6, dt);
            aimEyesX = (float)CharacterLife.Damp(aimEyesX, System.Math.Clamp((eyesX + dart) / CharacterLife.EyeReachH, -1, 1), 14, dt);
            aimEyesY = (float)CharacterLife.Damp(aimEyesY, eyesY / CharacterLife.EyeLiftH, 14, dt);
            return new CharacterAim(aimYaw, aimPitch, aimEyesX, aimEyesY);
        }

        /// <summary>The turn and pitch of a look at <paramref name="gaze"/>, in degrees from facing the person.</summary>
        private (double Yaw, double Pitch) LookAngles(CharacterGaze gaze, int avertSide)
        {
            switch (gaze)
            {
                case CharacterGaze.Person:
                case CharacterGaze.Moment:
                    return Person != null ? Toward(Person.position) : (0, 0);
                case CharacterGaze.Lap:
                    return Person != null ? Toward(Person.position + Vector3.down * LapBelowEyes) : (0, -ScreenDownDegrees);
                case CharacterGaze.Away:
                    return (-avertSide * AwayAsideDegrees, -AwayDownDegrees);
                case CharacterGaze.Neighbour:
                    return (-avertSide * NeighbourAsideDegrees, 0);
                default:
                    return (0, -ScreenDownDegrees);
            }
        }

        /// <summary>The turn and pitch toward <paramref name="point"/> from its head, about its place, which faces the person.</summary>
        private (double Yaw, double Pitch) Toward(Vector3 point)
        {
            var local = transform.InverseTransformDirection(point - figure.Head.position);
            var level = Mathf.Sqrt(local.x * local.x + local.z * local.z);
            if (level < 1e-6f) return (0, 0);
            return (Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, Mathf.Atan2(local.y, level) * Mathf.Rad2Deg);
        }

        /// <summary>Its tag: "Put Away" over its state's word while it leaves, and gone once it has folded.</summary>
        private void ShowTag()
        {
            var word = life.TagWord;
            if (word != saying && presentation != null)
            {
                saying = word;
                label.Show(word == null ? CharacterLabel.Of(presentation) : CharacterLabel.Of(presentation).Saying(word));
                shelfWords = null;
                shelfBadgeMeshes = null;
            }
            if (life.TagOpacity <= 0 && labels.gameObject.activeSelf) labels.gameObject.SetActive(false);
        }

        /// <summary>Today's body as a figure: one shell, so its head is its body; its eyes and surface are in its shader.</summary>
        private sealed class LegacyBody : ICharacterFigure
        {
            private readonly CharacterView view;

            public LegacyBody(CharacterView view) => this.view = view;

            /// <summary>The shell is about two units tall at its scale.</summary>
            public float Height => 2f * BodyScale;

            public Transform Head => view.body;

            public void Show(CharacterPose pose, KitFigureLook look, CharacterAim aim, float seconds, bool snap) => view.ShowBody(pose, aim, seconds, snap);
        }

        /// <summary>
        /// Shows a pose of its life on today's body: the posture its cues hold (<see cref="HeldLift"/>, which placement reads)
        /// with the life's float and hop on top, the life's blinks and pulses over the eyes its state draws, the look's turn
        /// and the accents on the body, the eyes' shift in the shader, and the arrival's and fold's size. Its eyes' shapes,
        /// surface, halo, ring and colour stay as its cues and its identity draw them.
        /// </summary>
        private void ShowBody(CharacterPose pose, CharacterAim aim, float deltaTime, bool snap)
        {
            var cues = this.cues!;
            var ease = snap ? 1f : 1f - Mathf.Exp(-deltaTime * 8f);
            var quick = snap ? 1f : 1f - Mathf.Exp(-deltaTime * 30f);
            var unit = figure.Height / (float)KitFigureLook.HeightM;
            var moving = !GlazeMotion.Still;

            // The posture held, eased, or at once while kept still (ADR 0027 item 6); the life's float and hop over it,
            // already eased by the life, and standing at once while kept still, so the body holds exactly.
            var posture = moving ? ease : 1f;
            restLift = Mathf.Lerp(restLift, HeldLift, posture);
            restSquash = Mathf.Lerp(restSquash, cues.Motion switch
            {
                CharacterMotion.Settle => 0.02f,
                CharacterMotion.Slump => 0.07f,
                CharacterMotion.Frozen => -0.03f,
                _ => 0f,
            }, posture);
            lift = restLift + (moving ? unit * (float)(pose.FloatYM + pose.HopM) : 0f);
            squash = restSquash + (moving ? 1f - (float)pose.Squash : 0f);
            var heldYaw = cues.Motion == CharacterMotion.Frozen ? 0.12f : 0f;
            var heldRoll = cues.Motion == CharacterMotion.Slump ? 0.14f : cues.Motion == CharacterMotion.Frozen ? -0.05f : 0f;

            // The eyes: their drawn shape and opening by its state, the life's blinks and pulses over them.
            var targetOpen = 1f;
            var targetInk = 1f;
            var targetUneven = 0f;
            var openEyes = true;
            switch (cues.Eyes)
            {
                case CharacterEyes.OnTask:
                    targetOpen = 0.78f;
                    break;
                case CharacterEyes.Scanning:
                    targetOpen = 0.9f;
                    break;
                case CharacterEyes.OnPerson:
                    targetOpen = 1.12f;
                    break;
                case CharacterEyes.Unfocused:
                    targetOpen = 0.42f;
                    targetUneven = UnsureUneven;
                    targetInk = 0.75f;
                    break;
                case CharacterEyes.Closed:
                case CharacterEyes.Crossed:
                case CharacterEyes.Flat:
                    openEyes = false;
                    break;
            }
            if (openEyes) targetOpen *= Mathf.Max(0.1f, 1f - (float)pose.Lids);
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
            // The light around it; a waiting one's pulse breathes with the life, and stands while kept still.
            var targetHaloColor = haloColor;
            var targetHalo = 0f;
            switch (cues.Halo)
            {
                case CharacterHalo.NeedsYou:
                    targetHaloColor = NeedsYouLight;
                    targetHalo = 0.75f * (0.8f + 0.25f * ((float)pose.Breath - 0.775f) / 0.225f);
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
            eyeOpen = Mathf.Lerp(eyeOpen, targetOpen, quick);
            // Kept still, the eyes hold their state's still look, so every state still shows by its eyes (ADR 0027 item 6):
            // on its work looking down, one that can't be told aside and a little up.
            (lookX, lookY) = moving ? (aim.EyesX, aim.EyesY) : cues.Eyes switch
            {
                CharacterEyes.OnTask => (0f, -0.7f),
                CharacterEyes.Scanning => (0f, -0.15f),
                CharacterEyes.Unfocused => (UnsureStillLook.x, UnsureStillLook.y),
                CharacterEyes.Closed => (0f, -0.25f),
                CharacterEyes.Crossed => (0f, -0.15f),
                CharacterEyes.Flat => (0.25f, 0f),
                _ => (0f, 0f),
            };
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

            // The turn: the look's, its accents and the float's roll, the posture's lean; relative to its place. This body
            // has no head apart from it, so it turns no further than it always has (TurnReach), and its eyes take the rest.
            body.localRotation = Quaternion.Euler(
                Mathf.Clamp(-aim.HeadPitchDegrees + (float)(pose.HeadPitchDegrees + pose.NodDegrees - pose.LeanDegrees), -TurnReach.y, TurnReach.y),
                Mathf.Clamp(aim.HeadYawDegrees - (float)pose.TurnDegrees, -TurnReach.x, TurnReach.x) + heldYaw * Mathf.Rad2Deg,
                heldRoll * Mathf.Rad2Deg + (float)((moving ? pose.RollDegrees : 0) + pose.HeadRollDegrees));
            // Its place in its home, to the person's right and away from them, where a placement gave it one.
            var scale = Mathf.Max(transform.lossyScale.x, 1e-4f);
            homeOffset = HasHome ? new Vector3(-(float)pose.XM, 0f, -(float)pose.ZM) / scale : Vector3.zero;
            body.localScale = Vector3.one * (float)pose.Scale;
            // The halo is a light behind it, not part of the turn: it stays upright where it stood.
            var unturn = Quaternion.Inverse(body.localRotation);
            haloRenderer.transform.localRotation = unturn;
            haloRenderer.transform.localPosition = unturn * new Vector3(0f, 0f, -0.3f * BodyScale);
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

        private void Apply(float t)
        {
            shell.localScale = new Vector3(BodyScale * (1f + squash), BodyScale * (1f - squash), BodyScale * (1f + squash));

            var floor = float.PositiveInfinity;
            if (Grounded)
                foreach (var vertex in shellVertices)
                    floor = Mathf.Min(floor, (body.localRotation * Vector3.Scale(vertex, shell.localScale)).y);
            body.localPosition = new Vector3(homeOffset.x, Grounded && !float.IsPositiveInfinity(floor) ? -floor : lift, homeOffset.z);

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

            if (featureRenderer != null)
            {
                // Its feature, in the same surface and light, lightened, with no eyes of its own.
                bodyBlock.SetVector(BodyColorId, featureColor);
                bodyBlock.SetFloat(EyeInkId, 0f);
                featureRenderer.SetPropertyBlock(bodyBlock);
            }

            haloRenderer.enabled = !bodyOnlyPresentation && haloStrength > 0.004f;
            if (haloRenderer.enabled)
            {
                haloBlock.SetColor(ColorId, new Color(haloColor.r, haloColor.g, haloColor.b, haloStrength));
                haloRenderer.SetPropertyBlock(haloBlock);
            }

            ringRenderer.enabled = !bodyOnlyPresentation && ringAlpha > 0.004f;
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
