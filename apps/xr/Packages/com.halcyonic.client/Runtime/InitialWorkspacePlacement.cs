#nullable enable
namespace Halcyonic.Client
{
    /// <summary>Admits the first workspace pose only after continuous wearer, tracking and focus readiness.</summary>
    public sealed class InitialWorkspacePlacement
    {
        public const double ReadySeconds = 0.3;
        private double? readySince;

        /// <summary>No timeout admits an absent wearer or an untracked pose. The caller supplies monotonic seconds.</summary>
        public bool Poll(double now, bool wearerPresent, bool headTracked, bool focused)
        {
            if (!wearerPresent || !headTracked || !focused)
            {
                readySince = null;
                return false;
            }
            if (!readySince.HasValue || now < readySince.Value) readySince = now;
            return now - readySince.Value >= ReadySeconds;
        }
    }
}
