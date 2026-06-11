#include "overlay.h"
#include "log.h"
#include <windows.h>
#include <ddraw.h>
#include "MinHook.h"

#include <vector>
#include <mutex>
#include <unordered_map>

#define STB_IMAGE_IMPLEMENTATION
#include "stb_image.h"   // keep stdio loaders (stbi_load by path)

namespace enb { namespace overlay {

// ---- display list -----------------------------------------------------------
enum Kind { K_TEXT, K_RECT, K_LINE, K_IMAGE };
struct Cmd {
    Kind kind;
    int x, y, w, h;
    uint32_t rgb;
    bool filled;
    int alpha;
    std::string s;   // text, or image path
};
static std::vector<Cmd> g_staging;     // built by Lua during tick
static std::vector<Cmd> g_render;      // consumed by Flip hook
static std::mutex g_list_mx;

// ---- cached images (premultiplied 32-bit DIB in a memory DC) ----------------
struct Image { HBITMAP bmp; HDC mdc; int w, h; HBITMAP old; };
static std::unordered_map<std::string, Image> g_images;
static std::mutex g_img_mx;

static Image* load_image(const std::string& path) {
    std::lock_guard<std::mutex> lk(g_img_mx);
    auto it = g_images.find(path);
    if (it != g_images.end()) return &it->second;

    int w, h, ch;
    unsigned char* px = stbi_load(path.c_str(), &w, &h, &ch, 4); // force RGBA
    if (!px) { logf("overlay: image load failed: %s", path.c_str());
               g_images[path] = Image{nullptr,nullptr,0,0,nullptr}; return nullptr; }

    BITMAPINFO bi = {};
    bi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    bi.bmiHeader.biWidth = w;
    bi.bmiHeader.biHeight = -h;           // top-down
    bi.bmiHeader.biPlanes = 1;
    bi.bmiHeader.biBitCount = 32;
    bi.bmiHeader.biCompression = BI_RGB;

    void* bits = nullptr;
    HDC screen = GetDC(nullptr);
    HBITMAP bmp = CreateDIBSection(screen, &bi, DIB_RGB_COLORS, &bits, nullptr, 0);
    HDC mdc = CreateCompatibleDC(screen);
    ReleaseDC(nullptr, screen);
    if (!bmp || !bits) {
        // Don't leak the DC, and cache the failure so we don't re-allocate (and re-leak) every frame.
        stbi_image_free(px);
        if (bmp) DeleteObject(bmp);
        if (mdc) DeleteDC(mdc);
        logf("overlay: DIBSection failed: %s", path.c_str());
        g_images[path] = Image{nullptr, nullptr, 0, 0, nullptr};
        return nullptr;
    }

    // stb gives RGBA; GDI 32-bit DIB is BGRA. Premultiply alpha for AlphaBlend(AC_SRC_ALPHA).
    uint8_t* dst = (uint8_t*)bits;
    for (int i = 0; i < w * h; ++i) {
        uint8_t r = px[i*4+0], g = px[i*4+1], b = px[i*4+2], a = px[i*4+3];
        dst[i*4+0] = (uint8_t)(b * a / 255);
        dst[i*4+1] = (uint8_t)(g * a / 255);
        dst[i*4+2] = (uint8_t)(r * a / 255);
        dst[i*4+3] = a;
    }
    stbi_image_free(px);

    HBITMAP old = (HBITMAP)SelectObject(mdc, bmp);
    Image img{ bmp, mdc, w, h, old };
    auto res = g_images.emplace(path, img);
    logf("overlay: loaded image %s (%dx%d)", path.c_str(), w, h);
    return &res.first->second;
}

// ---- COLORREF helper (rgb is 0xRRGGBB) --------------------------------------
static inline COLORREF cr(uint32_t rgb) {
    return RGB((rgb>>16)&0xFF, (rgb>>8)&0xFF, rgb&0xFF);
}

// ---- present hooks (Flip + Blt) ---------------------------------------------
// Windowed DirectDraw under Wine usually presents by BLITTING the back buffer to
// the primary surface (Blt, vtable[5]) rather than Flip (vtable[11]). A box that
// never appears despite "Flip hook installed" is the classic symptom: the hook is
// installed but the game never calls Flip. So we hook BOTH and draw on whichever
// fires. The once-only "FIRED" logs below tell us which path the build actually
// uses (and if NEITHER fires, the game is on a different IDirectDrawSurface
// interface version than our v1 probe -- the vtable we hooked is never called).
typedef HRESULT (WINAPI *Flip_t)(LPDIRECTDRAWSURFACE, LPDIRECTDRAWSURFACE, DWORD);
typedef HRESULT (WINAPI *Blt_t)(LPDIRECTDRAWSURFACE, LPRECT, LPDIRECTDRAWSURFACE,
                                LPRECT, DWORD, LPDDBLTFX);
static Flip_t real_Flip = nullptr;
static Blt_t  real_Blt  = nullptr;
static HFONT g_font = nullptr;

static void paint(HDC dc);   // defined below

// Is this surface the visible primary (the one a windowed present blits into)?
static bool is_primary(LPDIRECTDRAWSURFACE surf) {
    if (!surf) return false;
    DDSURFACEDESC d = {}; d.dwSize = sizeof d;
    if (FAILED(surf->GetSurfaceDesc(&d))) return false;
    return (d.ddsCaps.dwCaps & DDSCAPS_PRIMARYSURFACE) != 0;
}

static void draw_on(LPDIRECTDRAWSURFACE surf) {
    HDC dc = nullptr;
    if (surf && SUCCEEDED(surf->GetDC(&dc)) && dc) {
        paint(dc);
        surf->ReleaseDC(dc);
    }
}

static void paint(HDC dc) {
    std::vector<Cmd> frame;
    { std::lock_guard<std::mutex> lk(g_list_mx); frame = g_render; }
    if (frame.empty()) return;

    int oldbk = SetBkMode(dc, TRANSPARENT);
    HFONT oldfont = nullptr;
    if (g_font) oldfont = (HFONT)SelectObject(dc, g_font);

    for (auto& c : frame) {
        switch (c.kind) {
        case K_TEXT: {
            SetTextColor(dc, cr(c.rgb));
            TextOutA(dc, c.x, c.y, c.s.c_str(), (int)c.s.size());
            break;
        }
        case K_RECT: {
            RECT r{ c.x, c.y, c.x + c.w, c.y + c.h };
            HBRUSH br = CreateSolidBrush(cr(c.rgb));
            if (c.filled) FillRect(dc, &r, br);
            else          FrameRect(dc, &r, br);
            DeleteObject(br);
            break;
        }
        case K_LINE: {
            HPEN pen = CreatePen(PS_SOLID, 1, cr(c.rgb));
            HPEN oldpen = (HPEN)SelectObject(dc, pen);
            MoveToEx(dc, c.x, c.y, nullptr);
            LineTo(dc, c.w, c.h);   // w/h reused as x1/y1
            SelectObject(dc, oldpen);
            DeleteObject(pen);
            break;
        }
        case K_IMAGE: {
            Image* img = load_image(c.s);
            if (!img || !img->bmp) break;
            int dw = c.w > 0 ? c.w : img->w;
            int dh = c.h > 0 ? c.h : img->h;
            BLENDFUNCTION bf{ AC_SRC_OVER, 0, (BYTE)c.alpha, AC_SRC_ALPHA };
            AlphaBlend(dc, c.x, c.y, dw, dh, img->mdc, 0, 0, img->w, img->h, bf);
            break;
        }
        }
    }
    if (oldfont) SelectObject(dc, oldfont);
    SetBkMode(dc, oldbk);
}

static HRESULT WINAPI hk_Flip(LPDIRECTDRAWSURFACE surf, LPDIRECTDRAWSURFACE override, DWORD flags) {
    static bool logged = false;
    if (!logged) { logged = true; logf("overlay: Flip hook FIRED -- presenting via Flip"); }
    // Flip presents the back buffer next: draw BEFORE the flip so the overlay
    // rides along onto the screen.
    draw_on(surf);
    return real_Flip(surf, override, flags);
}

static HRESULT WINAPI hk_Blt(LPDIRECTDRAWSURFACE surf, LPRECT dstRect,
                             LPDIRECTDRAWSURFACE src, LPRECT srcRect,
                             DWORD flags, LPDDBLTFX fx) {
    // Do the real blit first, then draw on top -- for a windowed present the blit
    // IS the frame reaching the primary, so the overlay must land on it AFTER.
    HRESULT hr = real_Blt(surf, dstRect, src, srcRect, flags, fx);
    if (is_primary(surf)) {
        static bool logged = false;
        if (!logged) { logged = true; logf("overlay: Blt->primary FIRED -- presenting via Blt"); }
        draw_on(surf);
    }
    return hr;
}

bool init() {
    // create a throwaway DirectDraw + offscreen surface only to read the IDirectDrawSurface vtable
    LPDIRECTDRAW dd = nullptr;
    if (FAILED(DirectDrawCreate(nullptr, &dd, nullptr)) || !dd) {
        logf("overlay: DirectDrawCreate failed"); return false;
    }
    dd->SetCooperativeLevel(nullptr, DDSCL_NORMAL);

    DDSURFACEDESC desc = {};
    desc.dwSize = sizeof desc;
    desc.dwFlags = DDSD_CAPS | DDSD_WIDTH | DDSD_HEIGHT;
    desc.ddsCaps.dwCaps = DDSCAPS_OFFSCREENPLAIN | DDSCAPS_SYSTEMMEMORY;
    desc.dwWidth = 16; desc.dwHeight = 16;

    LPDIRECTDRAWSURFACE surf = nullptr;
    if (FAILED(dd->CreateSurface(&desc, &surf, nullptr)) || !surf) {
        logf("overlay: CreateSurface(probe) failed"); dd->Release(); return false;
    }
    void** vtable = *(void***)surf;          // IDirectDrawSurface vtable
    void* blt_addr  = vtable[5];             // index 5  = Blt
    void* flip_addr = vtable[11];            // index 11 = Flip
    surf->Release();
    dd->Release();

    if (MH_CreateHook(flip_addr, (void*)&hk_Flip, (void**)&real_Flip) != MH_OK) {
        logf("overlay: hook Flip failed"); return false;
    }
    if (MH_EnableHook(flip_addr) != MH_OK) { logf("overlay: enable Flip hook failed"); return false; }

    // Blt is the windowed present path; hooking it too is what actually makes the
    // overlay visible on most Wine setups. A failure here is non-fatal -- Flip may
    // still carry it -- so warn and continue rather than abort the overlay.
    if (MH_CreateHook(blt_addr, (void*)&hk_Blt, (void**)&real_Blt) == MH_OK) {
        if (MH_EnableHook(blt_addr) != MH_OK) logf("overlay: enable Blt hook failed");
    } else {
        logf("overlay: hook Blt failed (continuing with Flip only)");
    }

    g_font = CreateFontA(-14, 0, 0, 0, FW_NORMAL, 0, 0, 0, ANSI_CHARSET,
                         OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, ANTIALIASED_QUALITY,
                         FF_DONTCARE | DEFAULT_PITCH, "Tahoma");
    logf("overlay: hooks installed -- Flip @ %p, Blt @ %p", flip_addr, blt_addr);
    return true;
}

void shutdown() {
    if (g_font) { DeleteObject(g_font); g_font = nullptr; }
    std::lock_guard<std::mutex> lk(g_img_mx);
    for (auto& kv : g_images) {
        if (kv.second.mdc) { SelectObject(kv.second.mdc, kv.second.old); DeleteDC(kv.second.mdc); }
        if (kv.second.bmp) DeleteObject(kv.second.bmp);
    }
    g_images.clear();
}

void begin_frame() { g_staging.clear(); }
void commit_frame() {
    std::lock_guard<std::mutex> lk(g_list_mx);
    g_render.swap(g_staging);
}

void text(int x, int y, const std::string& s, uint32_t rgb) {
    g_staging.push_back({K_TEXT, x, y, 0, 0, rgb, false, 255, s});
}
void rect(int x, int y, int w, int h, uint32_t rgb, bool filled) {
    g_staging.push_back({K_RECT, x, y, w, h, rgb, filled, 255, {}});
}
void line(int x0, int y0, int x1, int y1, uint32_t rgb) {
    g_staging.push_back({K_LINE, x0, y0, x1, y1, rgb, false, 255, {}});
}
void image(const std::string& path, int x, int y, int w, int h, int alpha) {
    g_staging.push_back({K_IMAGE, x, y, w, h, 0, false, alpha, path});
}

}} // namespace enb::overlay
