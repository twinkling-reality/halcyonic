using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

[TestFixture, NonParallelizable]
public class InstructionSettingsTests
{
    [TestCase(TextSize.Standard, false)]
    [TestCase(TextSize.Larger, false)]
    [TestCase(TextSize.Standard, true)]
    [TestCase(TextSize.Larger, true)]
    public void RealDraftRetainsItsReadProofWhileHiddenSettingsLosePreviewOrForgetAuthority(TextSize size, bool forgetting)
    {
        var previous = Glaze.Palette;
        Glaze.SetPalette(GlazePalette.Default);
        try
        {
            var host = new FileMenuHost { TextSize = size };
            var work = new WaitingWork(true);
            work.Change(execution => { execution.PendingApprovals.Clear(); execution.Status = ExecutionStatus.Completed; }, WorkstreamStatus.Completed);
            var file = new FileColumn(host, () => work.Present(live: host.Live), new CommandFactory(Samples.Client), () => null, _ => null, () => "");
            var saved = 0;
            var confirmed = new List<string>();
            var pairing = new MenuSetting("pairing", SettingsText.YourMac, "Pairing",
                () => new SettingNow("Paired", "Paired with 192.0.2.40", "Forget it", "Pair again", "Forget this computer",
                    confirm: "Yes, forget this computer", valueIsData: true, about: "192.0.2.40"),
                () => Assert.Fail("Forget must use its confirmed reading"), read => confirmed.Add(read.About!));
            var hub = new SettingsHubColumn(host, new[] { pairing }, new AppearanceSettings(), () => saved++);
            var navigator = new MenuNavigator(MenuBar.Places.ToDictionary(place => place, _ => (Func<IMenuColumn>)(() => hub)));
            var bar = new MenuBar(MenuPlace.Settings, "Nothing waits");
            navigator.OpenMenu(MenuPlace.Settings);
            navigator.ShowBeside(file, work.Workstream.WorkstreamId);
            MenuFrame DrawMenu()
            {
                var frame = navigator.Frames(bar).Menu!;
                navigator.Drawn(MenuColumn.Menu, frame, null);
                return frame;
            }
            void MenuPress(string id, string? key = null) => Assert.That(navigator.Act(MenuColumn.Menu, id, key, DrawMenu(), null), Is.True);
            MenuFrame DrawFile()
            {
                MenuFrame frame = null!;
                for (var draw = 0; draw < 3; draw++)
                {
                    frame = navigator.Frames(bar).Beside!;
                    navigator.Drawn(MenuColumn.File, frame, null);
                }
                host.Wait(0.5);
                return frame;
            }
            void FilePress(string id, string? key = null) => Assert.That(navigator.Act(MenuColumn.File, id, key, DrawFile(), null), Is.True);
            DrawFile();
            // A delivered voice result can focus the existing ordinary file while Settings details stand.
            // The production FileColumn owns the capture route and execution, rather than a synthetic focused column.
            if (forgetting) { file.HoldStarted(FileScreens.HoldToTalk); file.HoldEnded(FileScreens.HoldToTalk, true); }
            MenuPress(SettingsHubColumn.OpenCategory, forgetting ? SettingsText.YourMac : SettingsHubColumn.Appearance);
            if (forgetting)
            {
                MenuPress(SettingsColumn.OpenSetting, "pairing");
                var details = DrawMenu();
                navigator.Drawn(MenuColumn.Side, details, details.Side);
                Assert.That(navigator.Act(MenuColumn.Side, SettingsColumn.ChangeSetting, null, details, details.Side), Is.True);
            }
            else
            {
                MenuPress(AppearanceColumn.Choose, "graphite");
                MenuPress(AppearanceColumn.Preview);
            }
            var oldSettings = DrawMenu();
            if (forgetting) navigator.Drawn(MenuColumn.Side, oldSettings, oldSettings.Side);
            const string words = "Add the missing rate limit test.";
            Action<string>? keyboard = null;
            if (forgetting) file.Heard(words);
            else { FilePress(FileScreens.TellIt); keyboard = host.Keyboard!.Value.Done; keyboard(words); }
            Assert.That(file.Frame!.Focused, Is.True);
            Assert.That(file.Screen.Instruction.Words, Is.EqualTo(words));
            Assert.That(host.Sent, Is.Empty);
            navigator.Frames(bar);
            Assert.That(navigator.MenuVisibility(false), Is.True);
            Assert.That(Glaze.Palette, Is.SameAs(GlazePalette.Default));
            Assert.That(navigator.BesideAside, Is.False);
            Assert.That(navigator.Act(MenuColumn.Menu, forgetting ? SettingsColumn.Yes : AppearanceColumn.Apply, null, oldSettings, null), Is.False);
            if (forgetting) Assert.That(navigator.Act(MenuColumn.Side, SettingsColumn.Yes, null, oldSettings, oldSettings.Side), Is.False);
            for (var part = 0; part < 100 && !file.Screen.Instruction.Read; part++)
            {
                DrawFile();
                if (!file.Screen.Instruction.Read) FilePress(FileScreens.NextInstructionPart);
            }
            var ready = DrawFile();
            Assert.That(file.Screen.Instruction.Read, Is.True);
            var partRead = file.Screen.Instruction.Part;
            Assert.That(navigator.Compact(), Is.True);
            navigator.MenuVisibility(false);
            keyboard?.Invoke("Late overwrite");
            Assert.That(navigator.Act(MenuColumn.File, FileScreens.SendInstruction, null, ready, null), Is.False);
            Assert.That(navigator.Expand(), Is.True);
            navigator.Frames(bar);
            navigator.MenuVisibility(false);
            Assert.That((file.Screen.Instruction.Words, file.Screen.Instruction.Part, file.Screen.Instruction.Read), Is.EqualTo((words, partRead, true)));
            Assert.That(navigator.Act(MenuColumn.File, FileScreens.SendInstruction, null, ready, null), Is.False, "Expand grants no old target authority");
            FilePress(FileScreens.SendInstruction);
            if (forgetting)
            {
                Assert.That(file.Steering.Armed, Is.EqualTo(WorkspaceAction.Instruct));
                Assert.That(host.Sent, Is.Empty, "spoken origin retains reviewed Yes after Compact");
                for (var part = 0; part < 100 && !file.Steering.CanConfirm; part++)
                {
                    DrawFile();
                    if (!file.Steering.CanConfirm) FilePress(FileScreens.NextPart, FileScreens.RequestKey);
                }
                FilePress(FileScreens.Yes);
            }
            Assert.That(((ExecutionSendInstructionCommand)host.Sent.Single()).Payload.Text, Is.EqualTo(words));
            Assert.That(saved, Is.Zero);
            Assert.That(confirmed, Is.Empty);
            navigator.Frames(bar);
            Assert.That(navigator.MenuVisibility(true), Is.True);
            Assert.That(navigator.Act(MenuColumn.Menu, forgetting ? SettingsColumn.Yes : AppearanceColumn.Apply, null, oldSettings, null), Is.False);
            if (forgetting)
            {
                var returned = DrawMenu();
                Assert.That(navigator.BesideAside, Is.True);
                navigator.Drawn(MenuColumn.Side, returned, returned.Side);
                Assert.That(navigator.Act(MenuColumn.Side, SettingsColumn.ChangeSetting, null, returned, returned.Side), Is.True);
            }
            else
            {
                MenuPress(AppearanceColumn.Choose, "graphite");
                MenuPress(AppearanceColumn.Preview);
            }
            // Compact from returned Settings must revoke its newly acquired transient authority too.
            var returnedSettings = DrawMenu();
            Assert.That(navigator.Compact(), Is.True);
            navigator.MenuVisibility(false);
            Assert.That(navigator.Expand(), Is.True);
            navigator.MenuVisibility(true);
            Assert.That(Glaze.Palette, Is.SameAs(GlazePalette.Default));
            Assert.That(navigator.Act(MenuColumn.Menu, forgetting ? SettingsColumn.Yes : AppearanceColumn.Apply, null, returnedSettings, null), Is.False);
            Assert.That((saved, confirmed.Count, host.Sent.Count), Is.EqualTo((0, 0, 1)));
        }
        finally { Glaze.SetPalette(previous); }
    }
}
