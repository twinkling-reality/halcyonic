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

        /// <summary>The row at rest (DESIGN's FINISH-3): its two home-size actions 32 dp apart, standing where the bar stood.</summary>
        public const float RowGapDp = 32f;

        /// <summary>The information panel's width in the pair (27.5 degrees), and the gap to the column of actions beside it (its left edge at yaw +6.75); the column takes the rest, 240 dp.</summary>
        public const float PanelDp = 440f;

        public const float PairGapDp = 16f;

        public const float ColumnDp = PairDegrees * DpPerDegree - PanelDp - PairGapDp;

        /// <summary>The panel's padding round its words (its content 14 dp in, the body 14 more), the room under the body before a sentence bar, and its corner.</summary>
        public const float PanelPadDp = 28f;

        public const float BodyFootDp = 18f;

        public const float PanelCornerDp = 20f;

        public const float SentenceInsetDp = 14f;

        public const float SentenceDp = 52f;

        /// <summary>The sentence's padding inside its bar: 14 dp at the sides, 12 above and below.</summary>
        public const float SentencePadXDp = 14f;

        public const float SentencePadYDp = 12f;

        public const float SentenceCornerDp = 10f;

        /// <summary>The caret after a typed draft's words: 2 dp by 24 dp.</summary>
        public const float CaretDp = 2f;

        public const float CaretHeightDp = 24f;

        /// <summary>Every word: Inter Regular 20 dp on a 28 dp line; a command's exact text JetBrains Mono 19 dp.</summary>
        public const float TypeDp = 20f;

        public const float LineDp = 28f;

        public const float MonoTypeDp = 19f;

        /// <summary>Larger text multiplies the type by this and reflows; widths stay.</summary>
        public const float LargerScale = 1.15f;
    }
}
