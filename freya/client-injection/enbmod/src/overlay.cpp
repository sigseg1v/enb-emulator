#include "overlay.h"
#include "game.h"
#include "log.h"
#include "mem.h"
#include <windows.h>
#include <d3d8.h>
#include "MinHook.h"

#include <vector>
#include <mutex>
#include <unordered_map>
#include <cstring>
#include <cmath>
#include <atomic>

#define STB_IMAGE_IMPLEMENTATION
#include "stb_image.h" // keep stdio loaders (stbi_load by path)

namespace enb {
namespace overlay {

// ---- display list -----------------------------------------------------------
enum Kind { K_TEXT, K_RECT, K_LINE, K_IMAGE, K_RECT_GRAD, K_RRECT, K_RRECT_GRAD, K_TEXQUAD };
struct Cmd {
    Kind kind;
    int x, y, w, h;
    uint32_t rgb;
    bool filled;
    int alpha;
    std::string s;       // text, or image path
    uint32_t rgb2;       // gradient bottom color (K_RECT_GRAD / K_RRECT_GRAD)
    int radius;          // corner radius (K_RRECT / K_RRECT_GRAD)
    float scale = 1.0f;  // glyph scale (K_TEXT; 1.0 = native atlas size)
    void* tex = nullptr; // game-owned IDirect3DTexture8* we hold a ref on (K_TEXQUAD)
};
static std::vector<Cmd> g_staging; // built by Lua during tick
static std::vector<Cmd> g_render;  // consumed by the Present hook
static std::mutex g_list_mx;

// ---- D3D8 present hook -------------------------------------------------------
// client.exe imports d3d8.dll and renders through Direct3D 8 -- it never
// presents via DirectDraw, so the previous ddraw Flip/Blt hooks could not fire
// (verified: a full session log carried "hooks installed" but neither one-shot
// FIRED line). The present path is IDirect3DDevice8::Present, vtable index 15.
// We resolve the vtable from a throwaway device (the d3d8 device vtable is
// shared per-DLL, so hooking it catches the game's device too), and in the
// hook draw the display list with the GAME's device: state-blocked 2D quads
// (D3DFVF_XYZRHW) for rects/lines/images and a GDI-baked font-atlas texture
// for text. All textures are D3DPOOL_MANAGED so a device Reset cannot strand
// them (managed resources survive Reset; only DEFAULT-pool blocks it).
typedef HRESULT(WINAPI* Present_t)(IDirect3DDevice8*, const RECT*, const RECT*, HWND,
                                   const RGNDATA*);
static Present_t real_Present = nullptr;

struct Vtx {
    float x, y, z, rhw;
    D3DCOLOR color;
    float u, v;
};
#define FVF_VTX (D3DFVF_XYZRHW | D3DFVF_DIFFUSE | D3DFVF_TEX1)

// All cached GPU objects are keyed to the device they were created on; if the
// game ever tears the device down and creates a new one, the caches rebuild.
static IDirect3DDevice8* g_cacheDev = nullptr;
static IDirect3DTexture8* g_white = nullptr; // 1x1 white: untextured prims

// A single reusable D3DSBT_ALL state block, created lazily on first draw and
// re-Captured/Applied each frame to snapshot+restore the game's device state
// around our 2D pass. Creating (and deleting) a fresh full-device state block
// EVERY frame -- the old draw_frame did -- churns a large heap allocation
// through wined3d 30-60x/s, which is a HUD-gated memory-growth vector (the whole
// point of a state block is to allocate once and reuse). 0 = not yet created;
// released on device reset/unload alongside the texture caches.
static DWORD g_stateBlock = 0;

// Backbuffer size, sampled in the Present hook each frame (Reset can change it
// without the device pointer changing). Read from the tick thread via screen_size.
static std::atomic<int> g_screen_w{0}, g_screen_h{0};

// Our own pointer, drawn on top of the display list. Toggled from Lua
// (set_cursor); g_cursor_hwnd maps the screen-space cursor into client coords.
static std::atomic<bool> g_cursor_on{false};
static std::atomic<void*> g_cursor_hwnd{nullptr};

// ---- font atlas (chars 32..126, GDI-rendered once into a managed texture) ----
struct Glyph {
    float u0, v0, u1, v1;
    int w, h;
};
static IDirect3DTexture8* g_fontTex = nullptr;
static Glyph g_glyphs[95];
static int g_lineHeight = 0;
static const int FONT_TEX = 256;
// Set (release order) after build_font filled g_glyphs/g_lineHeight, cleared on
// cache release. Gates measure_text reads from the tick thread.
static std::atomic<bool> g_fontReady{false};

// ---- cached images (path -> managed texture) ---------------------------------
struct Image {
    IDirect3DTexture8* tex;
    int w, h;
    float u1, v1;
};
static std::unordered_map<std::string, Image> g_images;
static std::mutex g_img_mx;

// ---- keeping game-owned textures alive across resolve->draw (K_TEXQUAD) ------
// The action-bar ability icons are LIVE textures owned by the game's UI cards.
// Lua resolves the card's IDirect3DTexture8* during the display-list REBUILD and
// queues it; the bind happens later, in the Present hook. Between those two
// points the game runs a full frame of logic and can free the card (ability
// used/swapped, zoning, logout), Releasing its texture to refcount 0. The d3d8
// wrapper is then destroyed -- its vtable pointer often still reads back (freed
// block not yet reused, so a shallow guard passes) but its wined3d resource is
// gone, and binding it faults deep inside DrawPrimitiveUP (NULL deref).
//
// The fix is a plain COM ownership claim: at resolve time (the source was just
// validated by the Lua walk, no game logic since) we AddRef() it, so the game's
// later Release cannot take it below 1 and the wrapper survives intact. We queue
// the same pointer and bind it in Present -- now guaranteed live. Each queued
// command owns exactly one ref; when its list is discarded (begin_frame swaps in
// a new staging list, or release_caches on device reset/unload) we Release it.
// AddRef/Release are interlocked and format-agnostic -- no pixel copy, no new GPU
// resource, no LockRect/pitch math (block-compressed formats made a copy unsafe).
// Both ends run on the Present thread (texture_quad skips the AddRef when off it),
// so any Release-to-zero destruction's GL cleanup lands on the device's thread.

static void release_cmd_textures(std::vector<Cmd>& v) {
    for (auto& c : v)
        if (c.kind == K_TEXQUAD && c.tex) {
            ((IDirect3DTexture8*)c.tex)->Release();
            c.tex = nullptr;
        }
}

static void release_caches() {
    g_fontReady.store(false, std::memory_order_release);
    if (g_fontTex) {
        g_fontTex->Release();
        g_fontTex = nullptr;
    }
    if (g_white) {
        g_white->Release();
        g_white = nullptr;
    }
    if (g_stateBlock && g_cacheDev) {
        g_cacheDev->DeleteStateBlock(g_stateBlock);
        g_stateBlock = 0;
    }
    {
        std::lock_guard<std::mutex> lk(g_list_mx);
        release_cmd_textures(g_render);
    }
    release_cmd_textures(g_staging);
    std::lock_guard<std::mutex> lk(g_img_mx);
    for (auto& kv : g_images)
        if (kv.second.tex)
            kv.second.tex->Release();
    g_images.clear();
}

static int pow2_up(int v) {
    int p = 1;
    while (p < v)
        p <<= 1;
    return p;
}

static bool build_white(IDirect3DDevice8* dev) {
    if (FAILED(dev->CreateTexture(1, 1, 1, 0, D3DFMT_A8R8G8B8, D3DPOOL_MANAGED, &g_white)))
        return false;
    D3DLOCKED_RECT lr;
    if (FAILED(g_white->LockRect(0, &lr, nullptr, 0)))
        return false;
    *(uint32_t*)lr.pBits = 0xFFFFFFFFu;
    g_white->UnlockRect(0);
    return true;
}

// GDI-render the printable ASCII set white-on-black into a DIB, then upload it
// as A8R8G8B8 with alpha = rendered luminance (antialiased edges keep partial
// coverage). One texture, built once per device.
static bool build_font(IDirect3DDevice8* dev) {
    HDC screen = GetDC(nullptr);
    HDC mdc = CreateCompatibleDC(screen);

    BITMAPINFO bi = {};
    bi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    bi.bmiHeader.biWidth = FONT_TEX;
    bi.bmiHeader.biHeight = -FONT_TEX; // top-down
    bi.bmiHeader.biPlanes = 1;
    bi.bmiHeader.biBitCount = 32;
    bi.bmiHeader.biCompression = BI_RGB;
    void* bits = nullptr;
    HBITMAP bmp = CreateDIBSection(screen, &bi, DIB_RGB_COLORS, &bits, nullptr, 0);
    ReleaseDC(nullptr, screen);
    if (!bmp || !bits) {
        if (bmp)
            DeleteObject(bmp);
        DeleteDC(mdc);
        logf("overlay: font DIB failed");
        return false;
    }
    HBITMAP oldbmp = (HBITMAP)SelectObject(mdc, bmp);

    HFONT font = CreateFontA(-14, 0, 0, 0, FW_NORMAL, 0, 0, 0, ANSI_CHARSET, OUT_DEFAULT_PRECIS,
                             CLIP_DEFAULT_PRECIS, ANTIALIASED_QUALITY, FF_DONTCARE | DEFAULT_PITCH,
                             "Tahoma");
    HFONT oldfont = (HFONT)SelectObject(mdc, font);
    SetTextColor(mdc, RGB(255, 255, 255));
    SetBkColor(mdc, RGB(0, 0, 0));
    SetBkMode(mdc, OPAQUE);

    TEXTMETRICA tm = {};
    GetTextMetricsA(mdc, &tm);
    g_lineHeight = tm.tmHeight;

    memset(bits, 0, FONT_TEX * FONT_TEX * 4);
    int x = 0, y = 0;
    bool fit = true;
    for (int c = 32; c <= 126; ++c) {
        char ch = (char)c;
        SIZE sz = {};
        GetTextExtentPoint32A(mdc, &ch, 1, &sz);
        if (x + sz.cx + 1 > FONT_TEX) {
            x = 0;
            y += g_lineHeight + 1;
        }
        if (y + g_lineHeight > FONT_TEX) {
            fit = false;
            break;
        }
        TextOutA(mdc, x, y, &ch, 1);
        Glyph& g = g_glyphs[c - 32];
        g.w = sz.cx;
        g.h = g_lineHeight;
        g.u0 = (float)x / FONT_TEX;
        g.v0 = (float)y / FONT_TEX;
        g.u1 = (float)(x + sz.cx) / FONT_TEX;
        g.v1 = (float)(y + g_lineHeight) / FONT_TEX;
        x += sz.cx + 1;
    }
    GdiFlush();

    bool ok = false;
    if (fit && SUCCEEDED(dev->CreateTexture(FONT_TEX, FONT_TEX, 1, 0, D3DFMT_A8R8G8B8,
                                            D3DPOOL_MANAGED, &g_fontTex))) {
        D3DLOCKED_RECT lr;
        if (SUCCEEDED(g_fontTex->LockRect(0, &lr, nullptr, 0))) {
            const uint8_t* src = (const uint8_t*)bits;
            for (int row = 0; row < FONT_TEX; ++row) {
                uint32_t* dst = (uint32_t*)((uint8_t*)lr.pBits + row * lr.Pitch);
                for (int col = 0; col < FONT_TEX; ++col) {
                    uint8_t lum = src[(row * FONT_TEX + col) * 4 + 1]; // green ch
                    dst[col] = ((uint32_t)lum << 24) | 0x00FFFFFFu;
                }
            }
            g_fontTex->UnlockRect(0);
            ok = true;
        } else {
            g_fontTex->Release();
            g_fontTex = nullptr;
        }
    }
    if (!ok)
        logf("overlay: font atlas build failed%s", fit ? "" : " (charset overflow)");
    else
        g_fontReady.store(true, std::memory_order_release);

    SelectObject(mdc, oldfont);
    DeleteObject(font);
    SelectObject(mdc, oldbmp);
    DeleteObject(bmp);
    DeleteDC(mdc);
    return ok;
}

static bool ensure_caches(IDirect3DDevice8* dev) {
    if (dev == g_cacheDev && g_white && g_fontTex)
        return true;
    if (g_cacheDev && dev != g_cacheDev)
        logf("overlay: device changed -- rebuilding GPU caches");
    release_caches();
    g_cacheDev = dev;
    if (!build_white(dev) || !build_font(dev)) {
        release_caches();
        return false;
    }
    return true;
}

static Image* load_image(IDirect3DDevice8* dev, const std::string& path) {
    std::lock_guard<std::mutex> lk(g_img_mx);
    auto it = g_images.find(path);
    if (it != g_images.end())
        return it->second.tex ? &it->second : nullptr;

    int w, h, ch;
    unsigned char* px = stbi_load(path.c_str(), &w, &h, &ch, 4); // force RGBA
    if (!px) {
        logf("overlay: image load failed: %s", path.c_str());
        g_images[path] = Image{nullptr, 0, 0, 0, 0}; // cache the failure
        return nullptr;
    }

    // Round texture dims up to powers of two (D3D8 caps commonly require it);
    // the quad samples only the [0..u1, 0..v1] sub-rect that holds the pixels.
    int tw = pow2_up(w), th = pow2_up(h);
    IDirect3DTexture8* tex = nullptr;
    if (FAILED(
            dev->CreateTexture((UINT)tw, (UINT)th, 1, 0, D3DFMT_A8R8G8B8, D3DPOOL_MANAGED, &tex))) {
        stbi_image_free(px);
        logf("overlay: image texture failed: %s", path.c_str());
        g_images[path] = Image{nullptr, 0, 0, 0, 0};
        return nullptr;
    }
    D3DLOCKED_RECT lr;
    if (FAILED(tex->LockRect(0, &lr, nullptr, 0))) {
        stbi_image_free(px);
        tex->Release();
        g_images[path] = Image{nullptr, 0, 0, 0, 0};
        return nullptr;
    }
    for (int row = 0; row < h; ++row) {
        uint32_t* dst = (uint32_t*)((uint8_t*)lr.pBits + row * lr.Pitch);
        const uint8_t* s = px + row * w * 4;
        for (int col = 0; col < w; ++col) // RGBA -> BGRA (straight alpha)
            dst[col] = ((uint32_t)s[col * 4 + 3] << 24) | ((uint32_t)s[col * 4 + 0] << 16) |
                       ((uint32_t)s[col * 4 + 1] << 8) | (uint32_t)s[col * 4 + 2];
        memset(dst + w, 0, (size_t)(tw - w) * 4); // clear the pow2 padding
    }
    for (int row = h; row < th; ++row)
        memset((uint8_t*)lr.pBits + row * lr.Pitch, 0, (size_t)tw * 4);
    tex->UnlockRect(0);
    stbi_image_free(px);

    Image img{tex, w, h, (float)w / tw, (float)h / th};
    auto res = g_images.emplace(path, img);
    logf("overlay: loaded image %s (%dx%d)", path.c_str(), w, h);
    return &res.first->second;
}

// ---- quad / line emit helpers (XYZRHW: pixel coords, -0.5 texel offset) ------
static inline D3DCOLOR argb(uint32_t rgb, int a) {
    return ((D3DCOLOR)(a & 0xFF) << 24) | (rgb & 0x00FFFFFFu);
}

static void draw_quad(IDirect3DDevice8* dev, IDirect3DTexture8* tex, float x, float y, float w,
                      float h, float u0, float v0, float u1, float v1, D3DCOLOR c) {
    Vtx v[4] = {
        {x - 0.5f, y - 0.5f, 0, 1, c, u0, v0},
        {x + w - 0.5f, y - 0.5f, 0, 1, c, u1, v0},
        {x - 0.5f, y + h - 0.5f, 0, 1, c, u0, v1},
        {x + w - 0.5f, y + h - 0.5f, 0, 1, c, u1, v1},
    };
    dev->SetTexture(0, tex);
    dev->DrawPrimitiveUP(D3DPT_TRIANGLESTRIP, 2, v, sizeof(Vtx));
}

static void draw_lines(IDirect3DDevice8* dev, const Vtx* v, UINT count) {
    dev->SetTexture(0, g_white);
    dev->DrawPrimitiveUP(D3DPT_LINESTRIP, count - 1, v, sizeof(Vtx));
}

// Per-channel lerp between two 0xRRGGBB colors at t in [0,1], with alpha a.
static D3DCOLOR argb_lerp(uint32_t top, uint32_t bot, float t, int a) {
    if (t < 0)
        t = 0;
    else if (t > 1)
        t = 1;
    uint32_t r = (uint32_t)(((top >> 16) & 0xFF) +
                            (((float)((bot >> 16) & 0xFF) - (float)((top >> 16) & 0xFF)) * t));
    uint32_t g = (uint32_t)(((top >> 8) & 0xFF) +
                            (((float)((bot >> 8) & 0xFF) - (float)((top >> 8) & 0xFF)) * t));
    uint32_t b = (uint32_t)((top & 0xFF) + (((float)(bot & 0xFF) - (float)(top & 0xFF)) * t));
    return ((D3DCOLOR)(a & 0xFF) << 24) | (r << 16) | (g << 8) | b;
}

static void draw_quad_grad(IDirect3DDevice8* dev, float x, float y, float w, float h, uint32_t top,
                           uint32_t bot, int a) {
    D3DCOLOR ct = argb(top, a), cb = argb(bot, a);
    Vtx v[4] = {
        {x - 0.5f, y - 0.5f, 0, 1, ct, 0, 0},
        {x + w - 0.5f, y - 0.5f, 0, 1, ct, 1, 0},
        {x - 0.5f, y + h - 0.5f, 0, 1, cb, 0, 1},
        {x + w - 0.5f, y + h - 0.5f, 0, 1, cb, 1, 1},
    };
    dev->SetTexture(0, g_white);
    dev->DrawPrimitiveUP(D3DPT_TRIANGLESTRIP, 2, v, sizeof(Vtx));
}

// ---- rounded rectangle (triangle-fan corners; no textures, repo binary-asset rule) ----
static const int kCornerSegs = 4;                   // arc steps per corner
static const int kRRectPts = 4 * (kCornerSegs + 1); // perimeter points (20)

// Clockwise perimeter of a rounded rect, starting at the top-left corner's
// leftmost point. Screen coords (y down), so sin<0 walks upward.
static void rrect_perimeter(float x, float y, float w, float h, float r, float* xs, float* ys) {
    const struct {
        float cx, cy, a0;
    } corner[4] = {
        {x + r, y + r, 180.f},       // top-left:     180 -> 270
        {x + w - r, y + r, 270.f},   // top-right:    270 -> 360
        {x + w - r, y + h - r, 0.f}, // bottom-right:   0 ->  90
        {x + r, y + h - r, 90.f},    // bottom-left:   90 -> 180
    };
    int n = 0;
    for (int c = 0; c < 4; ++c)
        for (int s = 0; s <= kCornerSegs; ++s, ++n) {
            float a = (corner[c].a0 + 90.f * (float)s / kCornerSegs) * 3.14159265f / 180.f;
            xs[n] = corner[c].cx + r * cosf(a);
            ys[n] = corner[c].cy + r * sinf(a);
        }
}

static void draw_rrect_cmd(IDirect3DDevice8* dev, const Cmd& c) {
    float w = (float)c.w, h = (float)c.h;
    if (w <= 0 || h <= 0)
        return;
    float r = (float)c.radius;
    float rmax = (w < h ? w : h) * 0.5f;
    if (r > rmax)
        r = rmax;
    if (r < 0)
        r = 0;
    float xs[kRRectPts], ys[kRRectPts];
    rrect_perimeter((float)c.x, (float)c.y, w, h, r, xs, ys);

    const bool grad = (c.kind == K_RRECT_GRAD);
    auto color_at = [&](float py) {
        return grad ? argb_lerp(c.rgb, c.rgb2, (py - (float)c.y) / h, c.alpha)
                    : argb(c.rgb, c.alpha);
    };

    if (c.filled) {
        Vtx v[kRRectPts + 2]; // center + perimeter + closing repeat
        float cx = (float)c.x + w * 0.5f, cy = (float)c.y + h * 0.5f;
        v[0] = {cx - 0.5f, cy - 0.5f, 0, 1, color_at(cy), 0, 0};
        for (int i = 0; i < kRRectPts; ++i)
            v[i + 1] = {xs[i] - 0.5f, ys[i] - 0.5f, 0, 1, color_at(ys[i]), 0, 0};
        v[kRRectPts + 1] = v[1];
        dev->SetTexture(0, g_white);
        dev->DrawPrimitiveUP(D3DPT_TRIANGLEFAN, kRRectPts, v, sizeof(Vtx));
    } else {
        Vtx v[kRRectPts + 1];
        for (int i = 0; i < kRRectPts; ++i)
            v[i] = {xs[i] - 0.5f, ys[i] - 0.5f, 0, 1, color_at(ys[i]), 0, 0};
        v[kRRectPts] = v[0];
        draw_lines(dev, v, kRRectPts + 1);
    }
}

// Classic arrow pointer, hotspot at the tip. The outline polygon is star-shaped
// from the tip, so a triangle fan from vertex 0 fills it; a closed line strip in
// dark ink outlines it. Drawn last in the scene so nothing overdraws it.
static void draw_cursor(IDirect3DDevice8* dev) {
    void* hw = g_cursor_hwnd.load(std::memory_order_relaxed);
    POINT p;
    if (!GetCursorPos(&p))
        return;
    if (hw)
        ScreenToClient((HWND)hw, &p);

    // arrow outline (tip first), in pixels relative to the hotspot
    static const float ax[] = {0, 0, 3.5f, 6, 8.5f, 6, 11};
    static const float ay[] = {0, 16, 12.5f, 18, 17, 11.5f, 11};
    const int N = 7;
    float fx = (float)p.x, fy = (float)p.y;

    const D3DCOLOR fill = argb(0xFFFFFF, 255), edge = argb(0x000000, 235);
    dev->SetTexture(0, g_white);

    Vtx fan[N + 1];
    for (int i = 0; i < N; ++i)
        fan[i] = {fx + ax[i] - 0.5f, fy + ay[i] - 0.5f, 0, 1, fill, 0, 0};
    fan[N] = fan[1]; // close the fan back to the first rim vertex
    dev->DrawPrimitiveUP(D3DPT_TRIANGLEFAN, N - 1, fan, sizeof(Vtx));

    Vtx out[N + 1];
    for (int i = 0; i < N; ++i)
        out[i] = {fx + ax[i] - 0.5f, fy + ay[i] - 0.5f, 0, 1, edge, 0, 0};
    out[N] = out[0];
    dev->DrawPrimitiveUP(D3DPT_LINESTRIP, N, out, sizeof(Vtx));
}

static void draw_text(IDirect3DDevice8* dev, int x, int y, const std::string& s, D3DCOLOR c,
                      float scale) {
    if (scale <= 0.0f)
        scale = 1.0f;
    float cx = (float)x;
    dev->SetTexture(0, g_fontTex);
    for (char chs : s) {
        unsigned char ch = (unsigned char)chs;
        if (ch < 32 || ch > 126)
            ch = '?';
        const Glyph& g = g_glyphs[ch - 32];
        float gw = g.w * scale, gh = g.h * scale;
        Vtx v[4] = {
            {cx - 0.5f, (float)y - 0.5f, 0, 1, c, g.u0, g.v0},
            {cx + gw - 0.5f, (float)y - 0.5f, 0, 1, c, g.u1, g.v0},
            {cx - 0.5f, (float)y + gh - 0.5f, 0, 1, c, g.u0, g.v1},
            {cx + gw - 0.5f, (float)y + gh - 0.5f, 0, 1, c, g.u1, g.v1},
        };
        dev->DrawPrimitiveUP(D3DPT_TRIANGLESTRIP, 2, v, sizeof(Vtx));
        cx += gw;
    }
}

static void draw_frame(IDirect3DDevice8* dev) {
    std::vector<Cmd> frame;
    {
        std::lock_guard<std::mutex> lk(g_list_mx);
        frame = g_render;
    }
    const bool want_cursor = g_cursor_on.load(std::memory_order_relaxed);
    if (frame.empty() && !want_cursor)
        return;
    if (!ensure_caches(dev))
        return;

    // Snapshot the device state, draw, then restore -- the game's renderer
    // must never see our 2D state leak into its next frame. Reuse ONE state
    // block (created lazily, re-Captured each frame) rather than allocating a
    // fresh D3DSBT_ALL block per frame: the per-frame Create/Delete churned a
    // large wined3d heap allocation 30-60x/s, a HUD-gated memory-growth vector.
    if (!g_stateBlock && FAILED(dev->CreateStateBlock(D3DSBT_ALL, &g_stateBlock)))
        g_stateBlock = 0;
    if (g_stateBlock)
        dev->CaptureStateBlock(g_stateBlock);

    dev->SetVertexShader(FVF_VTX);
    dev->SetPixelShader(0);
    dev->SetRenderState(D3DRS_ALPHABLENDENABLE, TRUE);
    dev->SetRenderState(D3DRS_SRCBLEND, D3DBLEND_SRCALPHA);
    dev->SetRenderState(D3DRS_DESTBLEND, D3DBLEND_INVSRCALPHA);
    dev->SetRenderState(D3DRS_ZENABLE, FALSE);
    dev->SetRenderState(D3DRS_ZWRITEENABLE, FALSE);
    dev->SetRenderState(D3DRS_LIGHTING, FALSE);
    dev->SetRenderState(D3DRS_CULLMODE, D3DCULL_NONE);
    dev->SetRenderState(D3DRS_FOGENABLE, FALSE);
    dev->SetRenderState(D3DRS_ALPHATESTENABLE, FALSE);
    dev->SetRenderState(D3DRS_STENCILENABLE, FALSE);
    dev->SetRenderState(D3DRS_FILLMODE, D3DFILL_SOLID);
    dev->SetRenderState(D3DRS_CLIPPING, TRUE);
    dev->SetTextureStageState(0, D3DTSS_COLOROP, D3DTOP_MODULATE);
    dev->SetTextureStageState(0, D3DTSS_COLORARG1, D3DTA_TEXTURE);
    dev->SetTextureStageState(0, D3DTSS_COLORARG2, D3DTA_DIFFUSE);
    dev->SetTextureStageState(0, D3DTSS_ALPHAOP, D3DTOP_MODULATE);
    dev->SetTextureStageState(0, D3DTSS_ALPHAARG1, D3DTA_TEXTURE);
    dev->SetTextureStageState(0, D3DTSS_ALPHAARG2, D3DTA_DIFFUSE);
    dev->SetTextureStageState(0, D3DTSS_MINFILTER, D3DTEXF_POINT);
    dev->SetTextureStageState(0, D3DTSS_MAGFILTER, D3DTEXF_POINT);
    dev->SetTextureStageState(1, D3DTSS_COLOROP, D3DTOP_DISABLE);
    dev->SetTextureStageState(1, D3DTSS_ALPHAOP, D3DTOP_DISABLE);

    // Present is called after the game's EndScene; opening our own short scene
    // for the HUD is the standard present-hook pattern.
    bool scene = SUCCEEDED(dev->BeginScene());

    for (auto& c : frame) {
        switch (c.kind) {
        case K_TEXT:
            draw_text(dev, c.x, c.y, c.s, argb(c.rgb, c.alpha), c.scale);
            break;
        case K_RECT: {
            D3DCOLOR col = argb(c.rgb, c.alpha);
            if (c.filled) {
                draw_quad(dev, g_white, (float)c.x, (float)c.y, (float)c.w, (float)c.h, 0, 0, 1, 1,
                          col);
            } else {
                float x0 = (float)c.x - 0.5f, y0 = (float)c.y - 0.5f;
                float x1 = x0 + (float)c.w - 1.0f, y1 = y0 + (float)c.h - 1.0f;
                Vtx v[5] = {
                    {x0, y0, 0, 1, col, 0, 0}, {x1, y0, 0, 1, col, 0, 0}, {x1, y1, 0, 1, col, 0, 0},
                    {x0, y1, 0, 1, col, 0, 0}, {x0, y0, 0, 1, col, 0, 0},
                };
                draw_lines(dev, v, 5);
            }
            break;
        }
        case K_RECT_GRAD:
            draw_quad_grad(dev, (float)c.x, (float)c.y, (float)c.w, (float)c.h, c.rgb, c.rgb2,
                           c.alpha);
            break;
        case K_RRECT:
        case K_RRECT_GRAD:
            draw_rrect_cmd(dev, c);
            break;
        case K_LINE: {
            D3DCOLOR col = argb(c.rgb, c.alpha);
            Vtx v[2] = {
                {(float)c.x - 0.5f, (float)c.y - 0.5f, 0, 1, col, 0, 0},
                {(float)c.w - 0.5f, (float)c.h - 0.5f, 0, 1, col, 0, 0}, // w/h = x1/y1
            };
            draw_lines(dev, v, 2);
            break;
        }
        case K_IMAGE: {
            Image* img = load_image(dev, c.s);
            if (!img)
                break;
            float dw = c.w > 0 ? (float)c.w : (float)img->w;
            float dh = c.h > 0 ? (float)c.h : (float)img->h;
            draw_quad(dev, img->tex, (float)c.x, (float)c.y, dw, dh, 0, 0, img->u1, img->v1,
                      argb(0xFFFFFF, c.alpha));
            break;
        }
        case K_TEXQUAD: {
            // c.tex is the game's icon texture, on which texture_quad took an
            // AddRef at resolve time -- so the game's later Release cannot destroy
            // it and it is always live to bind here. (Binding it WITHOUT holding a
            // ref used to fault: freed between resolve and draw, the wrapper's
            // wined3d resource was gone and DrawPrimitiveUP NULL-deref'd it.)
            if (!c.tex)
                break;
            // Game icon textures (action-bar abilities, item portraits) are opaque
            // RGB on a BLACK background with no usable alpha channel (alpha bytes
            // read as 0). The default SRCALPHA/INVSRCALPHA blend would key on that
            // zero alpha and draw nothing; a plain opaque draw shows the black box.
            // Instead blend on COLOR: src*ONE + dst*INVSRCCOLOR (a "black key"
            // screen blend) so black pixels (src=0) leave the background untouched
            // -- transparent -- while bright pixels stay solid. additive (ONE/ONE)
            // is the pure-glow variant. The icon is MODULATEd by the diffuse, which
            // we set to the tint (c.rgb) scaled by c.alpha, giving the HUD hue and a
            // brightness/fade control. Restore the standard alpha blend after.
            uint32_t tr = (((c.rgb >> 16) & 0xFF) * (c.alpha & 0xFF)) / 255;
            uint32_t tg = (((c.rgb >> 8) & 0xFF) * (c.alpha & 0xFF)) / 255;
            uint32_t tb = ((c.rgb & 0xFF) * (c.alpha & 0xFF)) / 255;
            D3DCOLOR diffuse = 0xFF000000u | (tr << 16) | (tg << 8) | tb;
            dev->SetRenderState(D3DRS_SRCBLEND, D3DBLEND_ONE);
            dev->SetRenderState(D3DRS_DESTBLEND, c.filled ? D3DBLEND_ONE : D3DBLEND_INVSRCCOLOR);
            draw_quad(dev, (IDirect3DTexture8*)c.tex, (float)c.x, (float)c.y, (float)c.w,
                      (float)c.h, 0, 0, 1, 1, diffuse);
            dev->SetRenderState(D3DRS_SRCBLEND, D3DBLEND_SRCALPHA);
            dev->SetRenderState(D3DRS_DESTBLEND, D3DBLEND_INVSRCALPHA);
            break;
        }
        }
    }

    if (want_cursor)
        draw_cursor(dev); // last -> on top of the whole HUD

    if (scene)
        dev->EndScene();
    if (g_stateBlock)
        dev->ApplyStateBlock(g_stateBlock); // restore; block is reused, not deleted
}

// Sample the real render size from the device's backbuffer. Cheap (one COM call,
// released immediately); done each frame so a windowed resize / device Reset is
// picked up without a separate hook.
static void sample_screen(IDirect3DDevice8* dev) {
    IDirect3DSurface8* bb = nullptr;
    if (SUCCEEDED(dev->GetBackBuffer(0, D3DBACKBUFFER_TYPE_MONO, &bb)) && bb) {
        D3DSURFACE_DESC d;
        if (SUCCEEDED(bb->GetDesc(&d))) {
            g_screen_w.store((int)d.Width, std::memory_order_relaxed);
            g_screen_h.store((int)d.Height, std::memory_order_relaxed);
        }
        bb->Release();
    }
}

static std::atomic<unsigned long> g_present_n{0};
unsigned long present_count() {
    return g_present_n.load(std::memory_order_relaxed);
}

// Thread that owns the D3D device (the one Present is called on). texture_quad
// (called from the tick thread) checks it before AddRef'ing a game texture, so if
// the tick and Present threads ever differ we skip the icon rather than risk a
// Release-to-zero destroying a texture (GL cleanup) off the device's thread. In
// this single-threaded engine they are the same, so the whole AddRef/bind/Release
// lifecycle stays on one thread.
static std::atomic<unsigned long> g_present_tid{0};

static HRESULT WINAPI hk_Present(IDirect3DDevice8* dev, const RECT* src, const RECT* dst, HWND wnd,
                                 const RGNDATA* dirty) {
    static bool logged = false;
    if (!logged) {
        logged = true;
        logf("overlay: Present hook FIRED -- D3D8 present path live");
    }
    g_present_n.fetch_add(1, std::memory_order_relaxed);
    g_present_tid.store(GetCurrentThreadId(), std::memory_order_relaxed);
    // Never touch the device while it is not in a drawable state. On alt-tab out
    // of fullscreen-exclusive D3D8 the device becomes D3DERR_DEVICELOST; sampling
    // the backbuffer or issuing our 2D draws then is invalid and a crash vector.
    // D3DERR_DEVICENOTRESET means the game is about to Reset() -- drop our cached
    // GPU objects so ensure_caches rebuilds them cleanly afterward. Either way we
    // still forward the real Present so the game's own lost-device recovery runs
    // unchanged.
    HRESULT coop = dev->TestCooperativeLevel();
    if (coop != D3D_OK) {
        if (coop == D3DERR_DEVICENOTRESET)
            release_caches();
        return real_Present(dev, src, dst, wnd, dirty);
    }
    sample_screen(dev);
    draw_frame(dev);
    return real_Present(dev, src, dst, wnd, dirty);
}

// Present-hook installation. We take the Present pointer from the vtable of the
// GAME'S OWN IDirect3DDevice8 (game::render::device) rather than creating a
// throwaway probe device. The old probe broke on native Windows: with the game
// holding the adapter (fullscreen-exclusive), a second windowed HAL device can
// fail, and the D3DDEVTYPE_REF fallback does not exist outside SDK installs --
// CreateDevice returned D3DERR_NOTAVAILABLE (0x8876086c) and the overlay never
// drew. (Both probe paths happened to work under WINE, which is why this never
// showed locally.) Every D3D8 device shares one implementation vtable, so the
// game device's Present slot is the same code address the probe used to find --
// minus the second device.
//
// The device global is null until the renderer starts, which is AFTER our
// launch-time injection -- so poll(), called every tick on the game thread,
// installs the hook as soon as the global goes non-null. Hot-injection into a
// running client hooks on the first poll. Installing from the game thread is
// also safer than the old worker-thread install: the game cannot be inside
// Present while its own thread is executing our tick.
static bool g_present_hooked = false; // poll() runs on the game thread only
static bool g_present_hook_failed = false;

void poll() {
    if (g_present_hooked || g_present_hook_failed)
        return;
    uintptr_t dev = mem::ptr(game::render::device);
    if (!dev)
        return; // renderer not up yet -- retry next tick
    uintptr_t vt = mem::ptr(dev); // IDirect3DDevice8 vtable
    if (!vt)
        return;
    void* present_addr = (void*)mem::ptr(vt + 15 * sizeof(void*)); // vtable[15] = Present
    if (!present_addr) {
        logf("overlay: device %p has null Present slot -- no drawing", (void*)dev);
        g_present_hook_failed = true;
        return;
    }
    if (MH_CreateHook(present_addr, (void*)&hk_Present, (void**)&real_Present) != MH_OK) {
        logf("overlay: hook Present failed -- no drawing");
        g_present_hook_failed = true;
        return;
    }
    if (MH_EnableHook(present_addr) != MH_OK) {
        logf("overlay: enable Present hook failed -- no drawing");
        g_present_hook_failed = true;
        return;
    }
    logf("overlay: hook installed -- IDirect3DDevice8::Present @ %p (game device %p)",
         present_addr, (void*)dev);
    g_present_hooked = true;
}

void shutdown() {
    release_caches();
    g_cacheDev = nullptr;
}

void screen_size(int* w, int* h) {
    if (w)
        *w = g_screen_w.load(std::memory_order_relaxed);
    if (h)
        *h = g_screen_h.load(std::memory_order_relaxed);
}

bool measure_text(const std::string& s, int* w, int* h) {
    if (w)
        *w = 0;
    if (h)
        *h = 0;
    if (!g_fontReady.load(std::memory_order_acquire))
        return false;
    int width = 0;
    for (char chs : s) {
        unsigned char ch = (unsigned char)chs;
        if (ch < 32 || ch > 126)
            ch = '?';
        width += g_glyphs[ch - 32].w;
    }
    if (w)
        *w = width;
    if (h)
        *h = g_lineHeight;
    return true;
}

void begin_frame() {
    // Release the refs the previous staging list (swapped out at the last commit)
    // held on game textures, then drop it. On the Present/tick thread.
    release_cmd_textures(g_staging);
    g_staging.clear();
}
void commit_frame() {
    std::lock_guard<std::mutex> lk(g_list_mx);
    g_render.swap(g_staging);
}

void text(int x, int y, const std::string& s, uint32_t rgb, float scale, int alpha) {
    g_staging.push_back({K_TEXT, x, y, 0, 0, rgb, false, alpha, s, 0, 0, scale});
}
void rect(int x, int y, int w, int h, uint32_t rgb, bool filled, int alpha) {
    g_staging.push_back({K_RECT, x, y, w, h, rgb, filled, alpha, {}, 0, 0});
}
void line(int x0, int y0, int x1, int y1, uint32_t rgb, int alpha) {
    g_staging.push_back({K_LINE, x0, y0, x1, y1, rgb, false, alpha, {}, 0, 0});
}
void rect_grad(int x, int y, int w, int h, uint32_t rgb_top, uint32_t rgb_bottom, int alpha) {
    g_staging.push_back({K_RECT_GRAD, x, y, w, h, rgb_top, true, alpha, {}, rgb_bottom, 0});
}
void rrect(int x, int y, int w, int h, int radius, uint32_t rgb, int alpha, bool filled) {
    g_staging.push_back({K_RRECT, x, y, w, h, rgb, filled, alpha, {}, 0, radius});
}
void rrect_grad(int x, int y, int w, int h, int radius, uint32_t rgb_top, uint32_t rgb_bottom,
                int alpha) {
    g_staging.push_back({K_RRECT_GRAD, x, y, w, h, rgb_top, true, alpha, {}, rgb_bottom, radius});
}
void image(const std::string& path, int x, int y, int w, int h, int alpha) {
    g_staging.push_back({K_IMAGE, x, y, w, h, 0, false, alpha, path, 0, 0});
}
void texture_quad(void* tex, int x, int y, int w, int h, int alpha, uint32_t tint, bool additive) {
    // Claim a ref on the game's icon NOW, while the Lua walk has just validated it,
    // so it cannot be freed before we bind it in Present. Only do this on the
    // Present thread: the matching Release (in begin_frame / release_caches) can
    // destroy the texture if ours is the last ref, and that GL teardown must run on
    // the device's thread. Off it, skip the icon rather than risk a cross-thread
    // destroy. A shallow liveness check guards the AddRef vtable call.
    unsigned long ptid = g_present_tid.load(std::memory_order_relaxed);
    if (!ptid || GetCurrentThreadId() != ptid)
        return;
    if (!tex || !enb::mem::readable(tex, 4) || !enb::mem::readable(*(void**)tex, 4))
        return;
    ((IDirect3DTexture8*)tex)->AddRef();
    Cmd c{};
    c.kind = K_TEXQUAD;
    c.x = x;
    c.y = y;
    c.w = w;
    c.h = h;
    c.rgb = tint;        // multiplied into the icon (light-blue HUD hue)
    c.filled = additive; // true = ONE/ONE glow; false = ONE/INVSRCCOLOR black-key
    c.alpha = alpha;
    c.tex = tex;
    g_staging.push_back(c);
}

void set_cursor(bool on, void* hwnd) {
    g_cursor_hwnd.store(hwnd, std::memory_order_relaxed);
    g_cursor_on.store(on, std::memory_order_relaxed);
}

} // namespace overlay
} // namespace enb
