# enbmod - 32-bit Lua mod runtime + injector for Earth & Beyond (client.exe)
# Cross-compiled for win32 with mingw-w64. Built to run under Wine.
#
#   make            # build build/enbmod.dll + build/inject.exe
#   make clean
#
# WHY THESE FLAGS:
#   i686-w64-mingw32-*  -> 32-bit PE output (client.exe is 32-bit; the DLL MUST match).
#   -static-libgcc -static-libstdc++ -> no libgcc_s_dw2-1.dll / libstdc++-6.dll runtime dep
#                                       (the game dir won't have them; static = self-contained).
#   -Wl,--kill-at  -> strip @N stdcall decorations from the export table so the export name is
#                     clean (matters for the proxy-DLL path / GetProcAddress).
#   -O2 -fno-strict-aliasing -> Lua and hook code do type punning; disable strict aliasing.
#   Lua: built as a STATIC lib (no LUA_BUILD_AS_DLL) and linked in -> single self-contained DLL.

CC      := i686-w64-mingw32-gcc
CXX     := i686-w64-mingw32-g++
AR      := i686-w64-mingw32-ar
WINDRES := i686-w64-mingw32-windres

TP        := third_party
LUA_DIR   := $(TP)/lua/src
MH_DIR    := $(TP)/minhook
BUILD     := build

# ---- Lua (static) -----------------------------------------------------------
# Exclude the standalone interpreter (lua.c) and compiler (luac.c) front-ends.
LUA_SRC := $(filter-out $(LUA_DIR)/lua.c $(LUA_DIR)/luac.c,$(wildcard $(LUA_DIR)/*.c))
LUA_OBJ := $(patsubst $(LUA_DIR)/%.c,$(BUILD)/lua/%.o,$(LUA_SRC))
LUA_LIB := $(BUILD)/liblua.a
# luaconf.h auto-selects the Windows config because mingw defines _WIN32. No LUA_USE_POSIX.
# -include lua_build_config.h forces Lua's error handling onto __builtin_setjmp/longjmp
# (libc setjmp/longjmp's unwind is broken under mingw32+Wine -- see lua_build_config.h).
LUA_CFLAGS := -O2 -fno-strict-aliasing -Wall -Wextra -Wno-unused-parameter -include $(CURDIR)/src/lua_build_config.h

# ---- MinHook (static) -------------------------------------------------------
# hde32.c provides the length-disassembler trampoline.c needs (hde32_disasm). 32-bit -> hde32 only.
MH_SRC := $(MH_DIR)/src/buffer.c $(MH_DIR)/src/hook.c $(MH_DIR)/src/trampoline.c \
          $(MH_DIR)/src/hde/hde32.c
MH_OBJ := $(BUILD)/mh/buffer.o $(BUILD)/mh/hook.o $(BUILD)/mh/trampoline.o $(BUILD)/mh/hde32.o
MH_LIB := $(BUILD)/libminhook.a
MH_CFLAGS := -O2 -I$(MH_DIR)/include -I$(MH_DIR)/src/hde

# ---- enbmod.dll -------------------------------------------------------------
DLL_SRC := $(wildcard src/*.cpp)
DLL_OBJ := $(patsubst src/%.cpp,$(BUILD)/dll/%.o,$(DLL_SRC))
DLL     := $(BUILD)/enbmod.dll
DLL_CXXFLAGS := -O2 -std=c++17 -fno-strict-aliasing -Wall -Wextra -Wno-unused-parameter \
               -I$(LUA_DIR) -I$(MH_DIR)/include -I$(TP) -DUNICODE -D_UNICODE -MMD -MP
DLL_LDFLAGS  := -shared -static -static-libgcc -static-libstdc++ \
               -Wl,--kill-at -Wl,--enable-stdcall-fixup
# d3d8 is loaded at runtime (GetModuleHandle/GetProcAddress in overlay.cpp), not linked.
# gdi32: font-atlas bake (CreateDIBSection/TextOutA).
DLL_LIBS := $(LUA_LIB) $(MH_LIB) -lkernel32 -luser32 -lgdi32 -lole32 -loleaut32 -lpsapi

# ---- inject.exe -------------------------------------------------------------
INJ     := $(BUILD)/inject.exe
INJ_SRC := inject/inject.cpp
INJ_CXXFLAGS := -O2 -std=c++17 -Wall
INJ_LDFLAGS  := -static -static-libgcc -static-libstdc++ -lkernel32 -lpsapi

.PHONY: all clean dirs test
all: dirs $(DLL) $(INJ)
	@echo "==> built $(DLL) and $(INJ)"

dirs:
	@mkdir -p $(BUILD)/lua $(BUILD)/mh $(BUILD)/dll

$(BUILD)/lua/%.o: $(LUA_DIR)/%.c
	$(CC) $(LUA_CFLAGS) -c $< -o $@
$(LUA_LIB): $(LUA_OBJ)
	$(AR) rcs $@ $^

$(BUILD)/mh/%.o: $(MH_DIR)/src/%.c
	$(CC) $(MH_CFLAGS) -c $< -o $@
$(BUILD)/mh/hde32.o: $(MH_DIR)/src/hde/hde32.c
	$(CC) $(MH_CFLAGS) -c $< -o $@
$(MH_LIB): $(MH_OBJ)
	$(AR) rcs $@ $^

$(BUILD)/dll/%.o: src/%.cpp
	$(CXX) $(DLL_CXXFLAGS) -c $< -o $@
$(DLL): $(DLL_OBJ) $(LUA_LIB) $(MH_LIB)
	$(CXX) $(DLL_OBJ) $(DLL_LDFLAGS) $(DLL_LIBS) -o $@

# Header dependency files emitted by -MMD (so editing game.h/hooks.h/etc. forces
# a recompile of the .cpp that includes it -- without this, header-only edits were
# silently ignored and a stale DLL got injected).
-include $(DLL_OBJ:.o=.d)

$(INJ): $(INJ_SRC)
	$(CXX) $(INJ_CXXFLAGS) $(INJ_SRC) $(INJ_LDFLAGS) -o $@

clean:
	rm -rf $(BUILD)

# Headless Lua mod tests: scripts run against tests/mock_enb.lua on a NATIVE
# Lua build (no client, no WINE); screenshot scenarios render to
# build/tests/shots/*.png for visual verification.
test:
	tests/run_tests.sh
