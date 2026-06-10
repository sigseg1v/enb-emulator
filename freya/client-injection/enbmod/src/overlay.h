#pragma once
// overlay.h -- DirectDraw Flip-hook overlay: GDI text/shapes + image blitting.
//
// We grab the IDirectDrawSurface vtable from a throwaway surface, hook Flip (vtable[11]) with
// MinHook, and in the hook draw a per-frame display list onto the surface's GDI DC before the
// real Flip presents it. The display list is rebuilt by Lua each tick (immediate-mode): the
// tick swaps a freshly-staged list into the render list under a lock, so the Flip thread always
// reads a complete frame.
//
// STATUS: this compiles and follows the standard DirectDraw-overlay pattern, but it is
// NOT verified in-game. Flip vs. Blt presentation, Wine's ddraw vtable, and windowed vs.
// fullscreen may need adjustment at runtime. See README.

#include <cstdint>
#include <string>

namespace enb { namespace overlay {

bool init();          // create throwaway surface, resolve + hook Flip. false on failure (logged).
void shutdown();

// ---- immediate-mode display list (called from Lua, on the tick thread) ----
void begin_frame();   // clear the staging list
void commit_frame();  // swap staging -> render (under lock)

void text(int x, int y, const std::string& s, uint32_t rgb);
void rect(int x, int y, int w, int h, uint32_t rgb, bool filled);
void line(int x0, int y0, int x1, int y1, uint32_t rgb);
// Draw image loaded from disk (PNG/TGA/BMP/JPG via stb_image). w/h <=0 = native size.
// Cached by path after first load. alpha 0..255 scales overall opacity.
void image(const std::string& path, int x, int y, int w, int h, int alpha);

}} // namespace enb::overlay
