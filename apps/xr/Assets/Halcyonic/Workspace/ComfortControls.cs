#nullable enable
using System;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// The person's comfort settings (<see cref="Comfort"/>, ADR 0023) in a section of Settings, kept
    /// on the device: reading text a step larger, badges that stand still, and sounds quieter or off.
    /// They take effect as the stage starts and the moment one changes: every label takes the text's
    /// size (<see cref="GlazeText.SetScale"/>), the badges stop or start moving
    /// (<see cref="StateBadgeView.Still"/>), and every sound Halcyonic plays takes the volume.
    /// </summary>
    public sealed class ComfortControls : MonoBehaviour
    {
        private const string Preference = "halcyonic.comfort";

        private Comfort comfort = new Comfort();
        private SettingsSection section = null!;
        private GlazeButton text = null!;
        private GlazeButton motion = null!;
        private GlazeButton sounds = null!;
        private bool rendering;

        /// <summary>The settings as they stand.</summary>
        public Comfort Settings => comfort;

        /// <summary>The section in Settings, for the editor's checks.</summary>
        public SettingsSection Section => section;

        /// <summary>
        /// The section showing <paramref name="shown"/>, for the editor's renders: nothing is read from
        /// or kept on the device, and nothing takes effect.
        /// </summary>
        public static ComfortControls ForRender(GameObject stageObject, Comfort shown)
        {
            var controls = stageObject.AddComponent<ComfortControls>();
            controls.rendering = true;
            controls.comfort = shown;
            controls.Build();
            return controls;
        }

        private void Awake()
        {
            comfort = Comfort.Load(PlayerPrefs.GetString(Preference, ""));
            Apply();
            Build();
        }

        private void Build()
        {
            section = SettingsSheet.On(gameObject).Section(Comfort.Heading, 2);
            text = section.Button("Text size", ButtonRole.Secondary);
            text.Pressed += () => Change(chosen => chosen.Text = chosen.Text == TextSize.Larger ? TextSize.Standard : TextSize.Larger);
            motion = section.Button("Motion", ButtonRole.Secondary);
            motion.Pressed += () => Change(chosen => chosen.Still = !chosen.Still);
            sounds = section.Button("Sounds", ButtonRole.Secondary);
            sounds.Pressed += () => Change(chosen => chosen.Sounds = chosen.NextSounds);
            Layout();
        }

        /// <summary>Changes a setting, keeps it on the device and lets it take effect now.</summary>
        private void Change(Action<Comfort> change)
        {
            if (FocusGuard.InputSuspended) return;
            change(comfort);
            if (!rendering)
            {
                PlayerPrefs.SetString(Preference, comfort.Save());
                PlayerPrefs.Save();
                Apply();
            }
            Layout();
        }

        private void Apply()
        {
            GlazeText.SetScale(comfort.TextScale);
            StateBadgeView.Still = comfort.Still;
            // Halcyonic's sounds are the only sounds the app plays.
            AudioListener.volume = comfort.Volume;
        }

        /// <summary>How the settings stand, and a button for each change.</summary>
        private void Layout()
        {
            section.Say(comfort.Line);
            section.Offer(text, comfort.TextButton);
            section.Offer(motion, comfort.MotionButton);
            section.Offer(sounds, comfort.SoundButton);
        }
    }
}
