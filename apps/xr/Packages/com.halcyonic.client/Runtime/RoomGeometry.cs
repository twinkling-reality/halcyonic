#nullable enable

namespace Halcyonic.Client
{
    /// <summary>
    /// The redesign's placement on one plane facing the person, 1.0 m away (DESIGN's SYSTEM-v4, 2026-11-05): angles in
    /// degrees at the eye, sizes in Meta's dp (16 dp a degree). One place for these numbers, so the views and their checks
    /// read the same ones.
    /// </summary>
    public static class RoomGeometry
    {
        /// <summary>Meta's dp a degree at the eye.</summary>
        public const float DpPerDegree = 16f;

        /// <summary>Where every surface stands.</summary>
        public const float DistanceMetres = 1.0f;

        /// <summary>The information panel and the choices panel together, from yaw -21.75 to +21.75.</summary>
        public const float PairDegrees = 43.5f;

        /// <summary>The line the characters, the information panel and the choices stand on.</summary>
        public const float FeetPitch = -17.6f;

        /// <summary>The bar's centre yaw and its foot's pitch, at rest and open.</summary>
        public const float BarYaw = 0f;

        public const float BarFootPitch = -23.5f;

        /// <summary>The bar's width at rest ("Type or Talk").</summary>
        public const float BarRestDp = 230f;

        /// <summary>The bar's width while it listens or says it sent.</summary>
        public const float BarWideDp = 340f;

        /// <summary>The bar's height with one line.</summary>
        public const float BarHeightDp = 64f;

        /// <summary>The bar's width open to write: the pair's.</summary>
        public const float BarOpenDp = PairDegrees * DpPerDegree;

        /// <summary>Every word: Inter Regular 20 dp on a 28 dp line; a command's exact text JetBrains Mono 19 dp.</summary>
        public const float TypeDp = 20f;

        public const float LineDp = 28f;

        public const float MonoTypeDp = 19f;

        /// <summary>Larger text multiplies the type by this and reflows; widths stay.</summary>
        public const float LargerScale = 1.15f;
    }
}
