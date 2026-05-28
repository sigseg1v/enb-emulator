# MinGW-w64 cross-compile toolchain for net7proxy (Win32 PE).
#
# Used by the proxy build to produce a 64-bit Windows executable that the
# launcher spawns alongside the WINE-hosted ENB client. Invoke from the
# proxy/ directory as:
#
#   cmake -S . -B build-win64 \
#         -DCMAKE_TOOLCHAIN_FILE=$(pwd)/cmake/mingw-w64-x86_64.toolchain.cmake \
#         -DCMAKE_BUILD_TYPE=Release
#   cmake --build build-win64 -j
#
# Requires the Debian/Ubuntu packages:
#   - gcc-mingw-w64-x86-64-posix
#   - g++-mingw-w64-x86-64-posix
#   - mingw-w64-x86-64-dev
#
# The -posix variant ships winpthreads so <pthread.h> works on the Win32
# target without any code changes.

set(CMAKE_SYSTEM_NAME Windows)
set(CMAKE_SYSTEM_PROCESSOR x86_64)

set(MINGW_PREFIX x86_64-w64-mingw32)

set(CMAKE_C_COMPILER   ${MINGW_PREFIX}-gcc-posix)
set(CMAKE_CXX_COMPILER ${MINGW_PREFIX}-g++-posix)
set(CMAKE_RC_COMPILER  ${MINGW_PREFIX}-windres)
set(CMAKE_AR           ${MINGW_PREFIX}-ar)
set(CMAKE_RANLIB       ${MINGW_PREFIX}-ranlib)

# Look for headers/libs in the MinGW sysroot first, then the OpenSSL prefix
# we built (see proxy/third_party/openssl-mingw64/).
set(CMAKE_FIND_ROOT_PATH
    /usr/${MINGW_PREFIX}
    ${CMAKE_CURRENT_LIST_DIR}/../third_party/openssl-mingw64
)
set(CMAKE_FIND_ROOT_PATH_MODE_PROGRAM NEVER)
set(CMAKE_FIND_ROOT_PATH_MODE_LIBRARY ONLY)
set(CMAKE_FIND_ROOT_PATH_MODE_INCLUDE ONLY)
set(CMAKE_FIND_ROOT_PATH_MODE_PACKAGE ONLY)

# Static-link libgcc/libstdc++/winpthreads so the resulting .exe doesn't
# need MinGW DLLs alongside it under WINE.
set(CMAKE_EXE_LINKER_FLAGS_INIT
    "-static -static-libgcc -static-libstdc++")
