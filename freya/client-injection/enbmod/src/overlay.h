#pragma once
// overlay.h -- Direct3D 8 Present-hook overlay: textured-quad text/shapes/images.
//
// client.exe renders through d3d8.dll (NOT DirectDraw -- a previous ddraw Flip/Blt hook never
// fired in-game). We resolve the IDirect3DDevice8 vtable from the GAME'S OWN device (the
// engine's static device global -- see game::render::device), hook Present (vtable[15]) with
// MinHook, and in the hook draw a per-frame display list with the GAME's device: state-blocked
// XYZRHW quads for rects/lines/images and a GDI-baked font-atlas texture for text. All textures
// are D3DPOOL_MANAGED, so a device Reset cannot strand them. The display list is rebuilt by Lua
// each tick (immediate-mode): the tick swaps a freshly-staged list into the render list under a
// lock, so the Present thread always reads a complete frame.

#include <cstdint>
#include <string>

namespace enb {
namespace overlay {

// Install the Present hook if the game's D3D device exists yet; no-op once
// installed (or after a hard hook failure, logged once). Called every tick on
// the game thread -- the device global is null until the renderer starts, so
// launch-time injection hooks a few frames in and hot-injection on the first
// tick.
void poll();
void shutdown();

// Backbuffer dimensions, observed by the Present hook each frame. {0,0} until
// the first present. This is the real render size (windowed: client area), so
// Lua can bottom-anchor UI at any resolution.
void screen_size(int* w, int* h);

// Pixel width/height of s in the overlay font. Returns false (w/h = 0) until
// the font atlas has been built (first presented frame); callers fall back to
// an estimate. Glyph metrics are plain ints written once at atlas build, so
// reading them from the tick thread is safe after the ready flag is set.
bool measure_text(const std::string& s, int* w, int* h);

// ---- immediate-mode display list (called from Lua, on the tick thread) ----
void begin_frame();  // clear the staging list
void commit_frame(); // swap staging -> render (under lock)

void text(int x, int y, const std::string& s, uint32_t rgb, float scale = 1.0f, int alpha = 255);
void rect(int x, int y, int w, int h, uint32_t rgb, bool filled, int alpha);
void line(int x0, int y0, int x1, int y1, uint32_t rgb, int alpha);
// Vertical gradient fill: rgb_top at y, rgb_bottom at y+h (per-vertex D3DCOLOR).
void rect_grad(int x, int y, int w, int h, uint32_t rgb_top, uint32_t rgb_bottom, int alpha);
// Rounded rectangle (triangle-fan corners). radius clamps to min(w,h)/2.
void rrect(int x, int y, int w, int h, int radius, uint32_t rgb, int alpha, bool filled);
void rrect_grad(int x, int y, int w, int h, int radius, uint32_t rgb_top, uint32_t rgb_bottom,
                int alpha);
// Draw image loaded from disk (PNG/TGA/BMP/JPG via stb_image). w/h <=0 = native size.
// Cached by path after first load. alpha 0..255 scales overall opacity.
void image(const std::string& path, int x, int y, int w, int h, int alpha);

// Blit a LIVE game-owned IDirect3DTexture8* (e.g. an action-bar ability icon captured
// from the game's own draw path) into a w x h quad at (x,y), sampling the full texture
// (UV 0..1). `tex` is the raw IDirect3DTexture8* the game already created on its own
// device; we only bind and draw it (no ownership, no Release). A null/garbage pointer
// is skipped in the Present hook. alpha 0..255 scales overall opacity.
void texture_quad(void* tex, int x, int y, int w, int h, int alpha, uint32_t tint = 0xFFFFFF,
                  bool additive = false);

// Draw our own mouse pointer on TOP of the display list (the native cursor
// renders underneath the HUD). `on` toggles it; `hwnd` is the game window, used
// to map the screen-space cursor into client coords. A procedural arrow (no
// binary asset, per the repo rule). Safe to call every tick.
void set_cursor(bool on, void* hwnd);

// Diagnostic: monotonic count of Present-hook calls (== HUD draw rate).
unsigned long present_count();

} // namespace overlay
} // namespace enb
