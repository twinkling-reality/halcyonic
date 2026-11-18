namespace Halcyonic.Client
{
    /// <summary>
    /// The one glass every plate takes (the lead's ruling, 2026-11-17 00:06Z; the owner's own reference): equipped-test's Ask
    /// panel (.eq-ask-bar), black glass rgba(12, 14, 22, 0.72) under one inset hairline of white 0.08. The same glass in two forms,
    /// by how a shader mixes it: the page's own values for a ground mixed as the page mixes, in sRGB (the agent's words band), and
    /// the face solved from the reference's pixels for Unity's linear blend (the room's panels and plates).
    /// </summary>
    public static class AskGlass
    {
        /// <summary>The page's colour, rgb(12, 14, 22), for a ground mixed in sRGB as the page mixes it.</summary>
        public const int PageColour = 0x0C0E16;

        /// <summary>The page's alpha, 0.72, mixed in sRGB.</summary>
        public const float PageAlpha = 0.72f;

        /// <summary>
        /// The face's colour in Unity's linear blend: with <see cref="FaceAlpha"/>, solved from the reference rendered in Chrome
        /// over the kit's three rooms (#1E1C1A, #B9AE9E, #EEEEEE), whose faces read #121217, #3E3A3A and #4B4C52; this lands
        /// within 7 of 255 of each.
        /// </summary>
        public const int FaceColour = 0x121212;

        /// <summary>The face's alpha in Unity's linear blend, solved with <see cref="FaceColour"/>.</summary>
        public const float FaceAlpha = 0.918f;

        /// <summary>The inset hairline's alpha, white at 0.08.</summary>
        public const float HairlineAlpha = 0.08f;
    }
}
