# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# preview_server.py -- interactive, in-browser previewer for the enbmod Lua HUD.
# It runs the REAL mod scripts (scripts/*.lua) inside interactive_host.lua
# (against tests/mock_enb.lua, on the native Lua build), captures the browser's
# mouse + keyboard, forwards them to the scripts' on_input handlers, ticks
# on_tick, and renders the returned draw commands on a <canvas> over the actual
# game-screen background (tests/enb-mod-bg.png). No client, no WINE, no D3D8 --
# but real click handlers, hover, and keybinds, positioned against the real HUD.
#
# Usage: preview_server.py [--port N] [--lua PATH] [--no-open]

import argparse
import json
import os
import subprocess
import threading
import webbrowser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

HERE = os.path.dirname(os.path.abspath(__file__))
BG_PATH = os.path.join(HERE, "enb-mod-bg.png")
DEFAULT_LUA = os.path.join(HERE, "..", "build", "tests", "lua")


class LuaHost:
    """A persistent interactive_host.lua process, accessed under a lock."""

    def __init__(self, lua_bin):
        self.proc = subprocess.Popen(
            [lua_bin, os.path.join(HERE, "interactive_host.lua"), HERE],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE,
            text=True, bufsize=1)
        self.lock = threading.Lock()

    def _cmd(self, line):
        self.proc.stdin.write(line + "\n")
        self.proc.stdin.flush()
        return self.proc.stdout.readline().rstrip("\n")

    def frame(self, events, screen=None, self_state=None, game_state=None):
        """Apply state + events, then tick. Returns (frame_dict, swallowed[], taps)."""
        with self.lock:
            if screen:
                self._cmd(f"screen {int(screen[0])} {int(screen[1])}")
            if game_state in ("space", "station", "login", "charsel", "load", "unknown"):
                self._cmd(f"state {game_state}")
            if self_state in ("cal", "uncal"):
                self._cmd(f"self {self_state}")
            swallowed = []
            for (msg, wp, lp) in events:
                r = self._cmd(f"input {int(msg)} {int(wp)} {int(lp)}")
                swallowed.append(r.strip() == "SWALLOW 1")
            frame_line = self._cmd("tick")
            taps_line = self._cmd("taps")
        taps = int(taps_line[len("TAPS "):]) if taps_line.startswith("TAPS ") else 0
        if not frame_line.startswith("FRAME "):
            return {"w": 0, "h": 0, "cmds": []}, swallowed, taps
        return json.loads(frame_line[len("FRAME "):]), swallowed, taps

    def close(self):
        try:
            self.proc.stdin.close()
            self.proc.terminate()
        except OSError:
            pass


PAGE = """<!doctype html><html><head><meta charset="utf-8">
<title>enbmod UI preview</title>
<style>
  html,body{margin:0;background:#0b0d12;color:#cfd6e4;font:13px sans-serif;overflow:hidden}
  #wrap{position:relative;display:inline-block}
  #cv{display:block;background:#000;cursor:crosshair;image-rendering:auto}
  #hud{position:fixed;top:8px;left:8px;background:rgba(10,14,24,.86);
       border:1px solid #2a3340;border-radius:8px;padding:8px 10px;line-height:1.5;
       pointer-events:none;white-space:pre;z-index:10}
  #ctl{position:fixed;top:8px;right:8px;background:rgba(10,14,24,.86);
       border:1px solid #2a3340;border-radius:8px;padding:8px 10px;z-index:10}
  #ctl label{display:block;margin:2px 0}
  #ctl button{font:12px sans-serif;margin:2px 2px 2px 0;background:#1c232c;color:#cfd6e4;
              border:1px solid #3a4654;border-radius:5px;padding:3px 8px;cursor:pointer}
  #ctl button.on{background:#2d4a6e;border-color:#78e1ff;color:#eaf4ff}
  .sw{color:#ff7070} .nosw{color:#74d18a}
</style></head><body>
<div id="wrap"><canvas id="cv"></canvas></div>
<div id="hud"></div>
<div id="ctl">
  <label><input type="checkbox" id="cal" checked> calibrated stats</label>
  <label><input type="checkbox" id="bg" checked> background</label>
  <div>state:
    <button data-s="space" class="on">space</button>
    <button data-s="station">station</button>
    <button data-s="login">login</button>
    <button data-s="unknown">unknown</button>
  </div>
  <div>resolution:
    <button data-r="1280x960">1280x960 (bg)</button>
    <button data-r="1280x992">1280x992</button>
    <button data-r="1024x768">1024x768</button>
    <button data-r="1920x1080">1920x1080</button>
  </div>
</div>
<script>
const cv = document.getElementById('cv'), ctx = cv.getContext('2d');
const hud = document.getElementById('hud');
let SW = 1280, SH = 960;
let bgImg = new Image(); bgImg.src = 'bg.png';
let showBg = true, calibrated = true, gameState = 'space';
let queue = [];                 // pending input events -> server
let lastSwallow = null, lastTaps = 0, mouse = {x:0,y:0};

function resizeCanvas(){
  cv.width = SW; cv.height = SH;
  // fit to viewport while keeping aspect; map mouse back to canvas px
  const vw = window.innerWidth, vh = window.innerHeight;
  const s = Math.min(vw/SW, vh/SH, 1);
  cv.style.width = (SW*s)+'px'; cv.style.height = (SH*s)+'px';
}
window.addEventListener('resize', resizeCanvas);

// ---- mouse ----
function canvasXY(e){
  const r = cv.getBoundingClientRect();
  const x = Math.round((e.clientX - r.left) * cv.width / r.width);
  const y = Math.round((e.clientY - r.top) * cv.height / r.height);
  return [x, y];
}
function packLp(x,y){ return ((y & 0xFFFF) << 16) | (x & 0xFFFF); }
cv.addEventListener('mousemove', e=>{ const [x,y]=canvasXY(e); mouse={x,y};
  queue.push([0x200, 0, packLp(x,y)]); });
cv.addEventListener('mousedown', e=>{ const [x,y]=canvasXY(e);
  const m = e.button===2?0x204:(e.button===1?0x207:0x201);
  queue.push([m, e.button===0?1:2, packLp(x,y)]); e.preventDefault(); });
cv.addEventListener('mouseup', e=>{ const [x,y]=canvasXY(e);
  const m = e.button===2?0x205:(e.button===1?0x208:0x202);
  queue.push([m, 0, packLp(x,y)]); e.preventDefault(); });
cv.addEventListener('contextmenu', e=>e.preventDefault());

// ---- keyboard -> VK ----
function vkOf(e){
  const c = e.code;
  if(/^Digit[0-9]$/.test(c)) return 0x30 + (+c.slice(5));
  if(/^Key[A-Z]$/.test(c))   return 0x41 + (c.charCodeAt(3)-65);
  if(c==='Minus') return 0xBD; if(c==='Equal') return 0xBB;
  if(c==='Space') return 0x20;
  return null;
}
window.addEventListener('keydown', e=>{ const vk=vkOf(e); if(vk===null) return;
  queue.push([e.altKey?0x104:0x100, vk, 0]); e.preventDefault(); });
window.addEventListener('keyup', e=>{ const vk=vkOf(e); if(vk===null) return;
  queue.push([e.altKey?0x105:0x101, vk, 0]); e.preventDefault(); });

// ---- controls ----
document.getElementById('cal').addEventListener('change', e=>calibrated=e.target.checked);
document.getElementById('bg').addEventListener('change', e=>showBg=e.target.checked);
document.querySelectorAll('#ctl button[data-r]').forEach(b=>b.addEventListener('click', ()=>{
  const [w,h] = b.dataset.r.split('x').map(Number); SW=w; SH=h; resizeCanvas();
}));
document.querySelectorAll('#ctl button[data-s]').forEach(b=>b.addEventListener('click', ()=>{
  gameState = b.dataset.s;
  document.querySelectorAll('#ctl button[data-s]').forEach(o=>o.classList.toggle('on', o===b));
}));

// ---- draw the returned command list ----
function hex(n){ return '#'+(n & 0xFFFFFF).toString(16).padStart(6,'0'); }
function render(frame, swallowed){
  ctx.clearRect(0,0,cv.width,cv.height);
  if(showBg && bgImg.complete && SW===bgImg.width && SH===bgImg.height)
    ctx.drawImage(bgImg, 0, 0);
  else { ctx.fillStyle='#070a12'; ctx.fillRect(0,0,cv.width,cv.height); }

  for(const c of frame.cmds){
    const a = (c.alpha==null?255:c.alpha)/255;
    ctx.globalAlpha = a;
    if(c.kind==='text'){
      ctx.globalAlpha=1; ctx.fillStyle=hex(c.rgb);
      ctx.font=Math.round(13*(c.scale||1))+'px DejaVu Sans, sans-serif'; ctx.textBaseline='top';
      ctx.fillText(c.text, c.x, c.y);
    } else if(c.kind==='rect'){
      ctx.fillStyle=hex(c.rgb);
      if(c.filled!==false) ctx.fillRect(c.x,c.y,c.w,c.h);
      else { ctx.strokeStyle=hex(c.rgb); ctx.strokeRect(c.x+.5,c.y+.5,c.w-1,c.h-1); }
    } else if(c.kind==='line'){
      ctx.strokeStyle=hex(c.rgb); ctx.beginPath();
      ctx.moveTo(c.x0+.5,c.y0+.5); ctx.lineTo(c.x1+.5,c.y1+.5); ctx.stroke();
    } else if(c.kind==='rect_grad' || c.kind==='rrect_grad'){
      const g=ctx.createLinearGradient(0,c.y,0,c.y+c.h);
      g.addColorStop(0,hex(c.rgb)); g.addColorStop(1,hex(c.rgb2));
      ctx.fillStyle=g;
      if(c.kind==='rrect_grad'){ roundPath(c.x,c.y,c.w,c.h,c.radius); ctx.fill(); }
      else ctx.fillRect(c.x,c.y,c.w,c.h);
    } else if(c.kind==='rrect'){
      roundPath(c.x,c.y,c.w,c.h,c.radius);
      if(c.filled!==false){ ctx.fillStyle=hex(c.rgb); ctx.fill(); }
      else { ctx.strokeStyle=hex(c.rgb); ctx.stroke(); }
    }
  }
  ctx.globalAlpha=1;
  if(swallowed && swallowed.length) lastSwallow = swallowed[swallowed.length-1];
  hud.textContent =
    `screen ${SW}x${SH}   mouse ${mouse.x},${mouse.y}\n`+
    `last input swallowed: ${lastSwallow===null?'-':lastSwallow}\n`+
    `taps this run: ${lastTaps}\n`+
    `cmds: ${frame.cmds.length}\n`+
    `keys: 1-9 0 - =  (click a button or press its key)`;
}
function roundPath(x,y,w,h,r){
  r=Math.min(r,w/2,h/2); ctx.beginPath();
  ctx.moveTo(x+r,y); ctx.arcTo(x+w,y,x+w,y+h,r); ctx.arcTo(x+w,y+h,x,y+h,r);
  ctx.arcTo(x,y+h,x,y,r); ctx.arcTo(x,y,x+w,y,r); ctx.closePath();
}

// ---- main loop: drain events, tick, render (~30fps) ----
async function loop(){
  const events = queue; queue = [];
  try{
    const res = await fetch('frame', {method:'POST', body: JSON.stringify({
      events, screen:[SW,SH], self: calibrated?'cal':'uncal', state: gameState})});
    const data = await res.json();
    if(data.taps!=null) lastTaps = data.taps;
    render(data.frame, data.swallowed);
  }catch(err){ hud.textContent = 'server error: '+err; }
  setTimeout(loop, 33);
}
bgImg.onload = ()=>{ if(SW===bgImg.width) resizeCanvas(); };
resizeCanvas(); loop();
</script></body></html>
"""


class Handler(BaseHTTPRequestHandler):
    host = None  # set on the class before serving

    def log_message(self, *a):
        pass  # quiet

    def _send(self, code, body, ctype="text/html; charset=utf-8"):
        if isinstance(body, str):
            body = body.encode()
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path in ("/", "/index.html"):
            self._send(200, PAGE)
        elif self.path == "/bg.png":
            try:
                with open(BG_PATH, "rb") as f:
                    self._send(200, f.read(), "image/png")
            except OSError:
                self._send(404, "no bg")
        else:
            self._send(404, "not found")

    def do_POST(self):
        if self.path != "/frame":
            self._send(404, "not found")
            return
        n = int(self.headers.get("Content-Length", 0))
        try:
            req = json.loads(self.rfile.read(n) or b"{}")
        except json.JSONDecodeError:
            req = {}
        events = [(e[0], e[1], e[2]) for e in req.get("events", [])]
        frame, swallowed, taps = self.host.frame(
            events, req.get("screen"), req.get("self"), req.get("state"))
        self._send(200, json.dumps(
            {"frame": frame, "swallowed": swallowed, "taps": taps}),
            "application/json")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=8777)
    ap.add_argument("--lua", default=DEFAULT_LUA)
    ap.add_argument("--no-open", action="store_true")
    args = ap.parse_args()

    if not os.path.exists(args.lua):
        raise SystemExit(f"native Lua not built at {args.lua} -- run tests/run_tests.sh first")
    if not os.path.exists(BG_PATH):
        print(f">>> note: no background at {BG_PATH} -- using a dark backdrop.")
        print(">>>       drop a 1280x960 game screenshot there to position against it.")

    host = LuaHost(args.lua)
    Handler.host = host

    srv = ThreadingHTTPServer(("127.0.0.1", args.port), Handler)
    url = f"http://127.0.0.1:{args.port}/"
    print(f">>> enbmod UI preview at {url}")
    print(">>> click buttons / press 1-9 0 - = / move mouse over the panel.")
    print(">>> Ctrl-C to stop.")
    if not args.no_open:
        webbrowser.open(url)
    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        host.close()
        srv.server_close()
        print("\n>>> stopped.")


if __name__ == "__main__":
    main()
