#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The texts the room's parts were given with facts joined on the dot. The parts draw each fact on its own line, so what is
    /// drawn holds no dot, and a check that reads the drawn words can no longer see a list of facts (AUDIT, 2026-11-13). While a
    /// walk listens, each part that splits on the dot tells this what it was given, and the walk fails on the words themselves.
    /// Nothing is kept while nobody listens, as in a player.
    /// </summary>
    public static class JoinedFacts
    {
        private static HashSet<string>? heard;

        /// <summary>Starts keeping the texts given with a joining dot, from now.</summary>
        public static void Listen() => heard = new HashSet<string>();

        /// <summary>Stops keeping them and forgets what was kept.</summary>
        public static void Stop() => heard = null;

        /// <summary>The texts given with a joining dot since the last call; empty while nobody listens.</summary>
        public static List<string> Take()
        {
            if (heard == null) return new List<string>();
            var given = heard.ToList();
            heard.Clear();
            return given;
        }

        /// <summary>Called by a part with the words it was given, before it splits them on the dot.</summary>
        internal static void Given(string text)
        {
            if (heard != null && text.Contains(" · ")) heard.Add(text);
        }
    }
}
