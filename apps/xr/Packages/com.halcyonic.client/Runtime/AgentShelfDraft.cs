#nullable enable
using System;
using System.Numerics;

namespace Halcyonic.Client
{
    public readonly struct AgentShelfPose
    {
        public AgentShelfPose(Vector3 position, float yaw) { Position = position; Yaw = yaw; }
        public Vector3 Position { get; }
        public float Yaw { get; }
    }

    /// <summary>A local placement preview. Only Apply may publish it; cancellation revokes pending room suggestions.</summary>
    public sealed class AgentShelfDraft
    {
        public const float Scale = 0.85f;
        public const float Spacing = 0.24f;
        public const float Depth = 0.26f;
        public static float Width(int capacity) => Math.Max(1, capacity) * Spacing * Scale + 0.1f;
        public Func<AgentShelfPose, bool>? Allows { get; set; }
        private Vector3 viewer;
        public AgentShelfPose? Candidate { get; private set; }
        public int Generation { get; private set; }
        public event Action? Changed;
        public void Begin(AgentShelfPose pose, Vector3 eyes)
        {
            Cancel();
            viewer = eyes;
            if (Fits(pose)) Candidate = pose;
            Changed?.Invoke();
        }
        public bool Suggest(int generation, AgentShelfPose pose)
        {
            if (Candidate == null || generation != Generation || !Fits(pose)) return false;
            Candidate = pose;
            Changed?.Invoke();
            return true;
        }
        public bool Move(Vector3 delta, float turn = 0f)
        {
            if (Candidate is not AgentShelfPose pose) return false;
            var next = new AgentShelfPose(pose.Position + delta, pose.Yaw + turn);
            return Suggest(Generation, next);
        }
        public bool Apply(Func<AgentShelfPose, bool> publish)
        {
            if (Candidate is not AgentShelfPose pose || !Fits(pose)) return false;
            // A callback cannot reapply this preview or publish a late room suggestion.
            Candidate = null;
            Generation++;
            Changed?.Invoke();
            return publish(pose);
        }
        public void Cancel()
        {
            Candidate = null;
            Generation++;
            Changed?.Invoke();
        }
        private bool Fits(AgentShelfPose pose)
        {
            var p = pose.Position;
            if (float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsNaN(p.Z) || float.IsNaN(pose.Yaw)
                || float.IsInfinity(p.X) || float.IsInfinity(p.Y) || float.IsInfinity(p.Z) || float.IsInfinity(pose.Yaw)) return false;
            var horizontal = new Vector2(p.X - viewer.X, p.Z - viewer.Z).Length();
            return horizontal >= 0.4f && horizontal <= 2f && p.Y <= viewer.Y - 0.15f && p.Y >= viewer.Y - 0.9f
                && (Allows == null || Allows(pose));
        }
    }
    /// <summary>A single held preview gesture. It cannot outlive its placement generation or input authority.</summary>
    public sealed class AgentShelfDrag
    {
        private readonly AgentShelfDraft draft;
        private Vector3? last;
        private int generation;
        public AgentShelfDrag(AgentShelfDraft draft) => this.draft = draft;
        public bool Active => last.HasValue;
        public bool Begin(Vector3 point, bool currentDraw)
        {
            End();
            if (!currentDraw || draft.Candidate == null || !Finite(point)) return false;
            generation = draft.Generation; last = point; return true;
        }
        public bool Follow(Vector3 point, bool authorized)
        {
            if (!authorized || generation != draft.Generation || draft.Candidate == null || !Finite(point)) { End(); return false; }
            if (last is not Vector3 previous) return false;
            last = point;
            return draft.Move(point - previous);
        }
        private static bool Finite(Vector3 point) => !float.IsNaN(point.X) && !float.IsNaN(point.Y) && !float.IsNaN(point.Z)
            && !float.IsInfinity(point.X) && !float.IsInfinity(point.Y) && !float.IsInfinity(point.Z);
        public void End() => last = null;
    }

}
