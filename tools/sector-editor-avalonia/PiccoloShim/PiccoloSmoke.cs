// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using System;
using System.Drawing;

namespace SectorEditorAvalonia.PiccoloShim
{
    /// <summary>
    /// Exercises every Piccolo shim API the sector-editor sprites/windows
    /// consume — picks, drags, translations, child counts, camera math.
    /// Returns 0 if all assertions pass.
    /// </summary>
    public static class PiccoloSmoke
    {
        public static int Run()
        {
            int errors = 0;

            var canvas = new PCanvas();
            var layer = canvas.Layer;

            var sigPen = new Pen(Color.Red, 3.0f);
            var rrPen = new Pen(Color.MistyRose, 2.0f) { DashStyle = DashStyle.Dash };
            var sig = PPath.CreateEllipse(0, 0, 50, 50);
            sig.Pen = sigPen;
            sig.Brush = Brushes.Transparent;
            var rr = PPath.CreateEllipse(0, 0, 100, 100);
            rr.Pen = rrPen;
            rr.Brush = Brushes.Transparent;

            var img = new PImage();
            img.Width = 32; img.Height = 32;
            img.X = 100; img.Y = 100;
            img.AddChild(sig);
            img.AddChild(rr);
            img.ChildrenPickable = false;
            img.Tag = "mob-1";

            var label = new PText("Some mob") { TextAlignment = StringAlignment.Center };
            img.AddChild(label);

            layer.AddChild(img);

            if (img.ChildCount != 3) { Console.Error.WriteLine($"shim: child-count {img.ChildCount} != 3"); errors++; }
            if (img.GetChild(0) != sig) { Console.Error.WriteLine("shim: GetChild(0) wrong"); errors++; }
            if (img.Parent != layer) { Console.Error.WriteLine("shim: parent not set"); errors++; }
            if (!ReferenceEquals(canvas.Layer, layer)) { Console.Error.WriteLine("shim: canvas.Layer drifted"); errors++; }

            float beforeX = img.X;
            img.TranslateBy(10, -5);
            if (img.X != beforeX + 10) { Console.Error.WriteLine($"shim: TranslateBy X wrong: {img.X}"); errors++; }

            var pickInside = layer.PickTopDown(new PointF(115, 115));
            if (pickInside != img) { Console.Error.WriteLine($"shim: pick inside found {pickInside?.Tag ?? "null"} not mob-1"); errors++; }
            var pickOutside = layer.PickTopDown(new PointF(500, 500));
            if (pickOutside != null) { Console.Error.WriteLine("shim: pick outside should be null"); errors++; }

            bool downFired = false, dragFired = false, upFired = false;
            img.MouseDown += (_, e) => { downFired = true; if (e.Position.X < 0) errors++; };
            img.MouseDrag += (_, _) => dragFired = true;
            img.MouseUp += (_, _) => upFired = true;
            var args = new PInputEventArgs { Position = new PointF(115, 115), PickedNode = img };
            img.RaiseMouseDown(args);
            img.RaiseMouseDrag(args);
            img.RaiseMouseUp(args);
            if (!(downFired && dragFired && upFired))
            {
                Console.Error.WriteLine($"shim: event fire down={downFired} drag={dragFired} up={upFired}");
                errors++;
            }

            canvas.Camera.Pan(50, 0);
            if (canvas.Camera.TranslateX != 50) { Console.Error.WriteLine("shim: pan failed"); errors++; }
            canvas.Camera.ZoomBy(2.0f, new PointF(0, 0));
            if (canvas.Camera.Scale != 2.0f) { Console.Error.WriteLine($"shim: zoom failed scale={canvas.Camera.Scale}"); errors++; }

            var beforeRemove = layer.ChildCount;
            layer.RemoveChild(img);
            if (layer.ChildCount != beforeRemove - 1) { Console.Error.WriteLine("shim: RemoveChild failed"); errors++; }
            if (img.Parent != null) { Console.Error.WriteLine("shim: Parent not cleared on remove"); errors++; }

            // Re-attach + RemoveAllChildren
            layer.AddChild(img);
            layer.AddChild(new PNode());
            layer.RemoveAllChildren();
            if (layer.ChildCount != 0) { Console.Error.WriteLine("shim: RemoveAllChildren failed"); errors++; }

            // MouseWheelZoomController ctor-compat
            _ = new MouseWheelZoomController(canvas.Camera);

            // Drag handler smoke
            var draggable = new PNode { X = 0, Y = 0, Width = 10, Height = 10 };
            new PDragEventHandler().Attach(draggable);
            draggable.RaiseMouseDown(new PInputEventArgs { Position = new PointF(5, 5) });
            draggable.RaiseMouseDrag(new PInputEventArgs { Position = new PointF(15, 25) });
            if (draggable.X != 10 || draggable.Y != 20)
            {
                Console.Error.WriteLine($"shim: drag handler did not move node: ({draggable.X},{draggable.Y})");
                errors++;
            }

            return errors;
        }
    }
}
