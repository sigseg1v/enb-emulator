// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using Avalonia.Controls;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace SectorEditorAvalonia.Utilities
{
    // Replaces the NullNotificationSink at runtime — pops a real
    // Avalonia message box. The MainWindow owns the parent reference
    // so the box modally attaches.
    public sealed class AvaloniaNotificationSink : INotificationSink
    {
        private readonly Window _owner;

        public AvaloniaNotificationSink(Window owner) { _owner = owner; }

        public void ShowError(string message)
        {
            var box = MessageBoxManager.GetMessageBoxStandard(
                "Sector Editor", message, ButtonEnum.Ok, Icon.Error);
            box.ShowWindowDialogAsync(_owner);
        }
    }
}
