// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

namespace SectorEditorAvalonia.PiccoloShim
{
    /// <summary>
    /// Original WinForms sector-editor wired Piccolo's
    /// MouseWheelZoomController to canvas.Camera. PCanvas now does that
    /// natively in OnPointerWheelChanged — this type stays as a
    /// constructor-API-compat shim so the call-site
    /// `new MouseWheelZoomController(canvas.Camera)` continues to
    /// compile without modification.
    /// </summary>
    public sealed class MouseWheelZoomController
    {
        public PCamera Camera { get; }

        public MouseWheelZoomController(PCamera camera)
        {
            Camera = camera;
        }
    }
}
