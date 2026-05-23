# server/CMakeLists.txt
#
# Phase B SCAFFOLDING — this configure file is intentionally permissive.
# The Net-7 codebase is ~162K lines of 2010-vintage, Windows-targeted C++.
# Configure (`cmake -S server -B build/server`) should succeed; the build
# step (`cmake --build build/server`) is EXPECTED to produce a large pile
# of errors during Phase B. See `server/BUILD_ERRORS.md` (created by the
# Phase B iteration) for the running tally.
#
# This file is Linux-first. Windows support is a Phase B-continuation /
# Phase I concern.

cmake_minimum_required(VERSION 3.20)

project(net7
    VERSION 0.1.0
    DESCRIPTION "Earth & Beyond / Net-7 emulator server (preservation fork)"
    LANGUAGES CXX
)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS ON)   # legacy code uses some GNU-isms; keep them.

set(CMAKE_EXPORT_COMPILE_COMMANDS ON)

if(NOT CMAKE_BUILD_TYPE AND NOT CMAKE_CONFIGURATION_TYPES)
    set(CMAKE_BUILD_TYPE Debug CACHE STRING "Build type" FORCE)
endif()

# --------------------------------------------------------------------------
# Source discovery
#
# Yes, GLOB_RECURSE is officially discouraged. We use it anyway because:
#   - it's 2000+ files
#   - no human is going to hand-curate the list
#   - CONFIGURE_DEPENDS makes CMake re-scan when files change at build time
#
# Excluded paths:
#   - server/src/Debug/, server/src/Release/      MSVC build outputs
#   - server/src/LUA/luabind/msvc-*/              MSVC prebuilds of luabind
#   - server/src/LUA/lua/msvc-*/                  MSVC prebuilds of lua
# --------------------------------------------------------------------------
file(GLOB_RECURSE NET7_SRC CONFIGURE_DEPENDS
    "${CMAKE_CURRENT_SOURCE_DIR}/src/*.cpp"
)

list(FILTER NET7_SRC EXCLUDE REGEX "/src/Debug/")
list(FILTER NET7_SRC EXCLUDE REGEX "/src/Release/")
list(FILTER NET7_SRC EXCLUDE REGEX "/src/LUA/luabind/msvc-")
list(FILTER NET7_SRC EXCLUDE REGEX "/src/LUA/lua/msvc-")

# Compat shim sources (real implementations, not just headers).
file(GLOB NET7_COMPAT_SRC CONFIGURE_DEPENDS
    "${CMAKE_CURRENT_SOURCE_DIR}/compat/*.cpp"
)

message(STATUS "net7: ${CMAKE_CURRENT_SOURCE_DIR}")
list(LENGTH NET7_SRC _net7_src_count)
message(STATUS "net7: discovered ${_net7_src_count} .cpp files under src/ (post-exclude)")

# --------------------------------------------------------------------------
# Dependencies
# --------------------------------------------------------------------------
find_package(OpenSSL 3.0 REQUIRED)
find_package(Threads REQUIRED)
find_package(Lua REQUIRED)

# TinyXML — Debian's `libtinyxml-dev` provides FindTinyXML via cmake-extras,
# but on a bare system the find_package may fail. We mark it optional here;
# Phase B will wire up an actual fallback (vendored copy in third_party/
# is a candidate).
find_package(TinyXML QUIET)
if(NOT TinyXML_FOUND)
    message(WARNING
        "net7: TinyXML not found via find_package. "
        "Install libtinyxml-dev or wire up the vendored copy in "
        "server/third_party/ (Phase B continuation).")
endif()

# Crypto++. Debian ships it as `libcrypto++-dev` providing libcrypto++.so
# but neither a CMake config nor a pkg-config file by default on every
# release. Try pkg-config first, then fall back to looking for the .so.
find_package(PkgConfig QUIET)
set(CRYPTOPP_LIBRARIES "")
set(CRYPTOPP_INCLUDE_DIRS "")
if(PkgConfig_FOUND)
    pkg_check_modules(CRYPTOPP IMPORTED_TARGET libcrypto++)
endif()
if(NOT CRYPTOPP_FOUND)
    find_library(CRYPTOPP_LIB NAMES crypto++ cryptopp)
    find_path(CRYPTOPP_INCLUDE NAMES cryptopp/cryptlib.h)
    if(CRYPTOPP_LIB AND CRYPTOPP_INCLUDE)
        set(CRYPTOPP_LIBRARIES "${CRYPTOPP_LIB}")
        set(CRYPTOPP_INCLUDE_DIRS "${CRYPTOPP_INCLUDE}")
        message(STATUS "net7: cryptopp via find_library: ${CRYPTOPP_LIB}")
    else()
        message(WARNING
            "net7: Crypto++ not found. Install libcrypto++-dev. "
            "Build will fail at link time.")
    endif()
endif()

# libpq / libpqxx — not strictly required during Phase B scaffolding (the
# server still talks to MySQL), but Phase C will add Postgres. We surface
# their presence here so the build environment is correct ahead of time.
find_package(PostgreSQL QUIET)
if(NOT PostgreSQL_FOUND)
    message(STATUS "net7: PostgreSQL client lib not found (Phase C concern).")
endif()

# --------------------------------------------------------------------------
# Target
# --------------------------------------------------------------------------
add_executable(net7
    ${NET7_SRC}
    ${NET7_COMPAT_SRC}
)

target_include_directories(net7 PRIVATE
    "${CMAKE_CURRENT_SOURCE_DIR}/compat"
    "${CMAKE_CURRENT_SOURCE_DIR}/src"
    "${CMAKE_CURRENT_SOURCE_DIR}/third_party"
    ${LUA_INCLUDE_DIR}
    ${CRYPTOPP_INCLUDE_DIRS}
)

target_compile_definitions(net7 PRIVATE
    USE_OPENSSL
    LINUX
    __linux__
    # Initial compat target. Phase E migrates to 0x30000000L cleanly.
    OPENSSL_API_COMPAT=0x10100000L
)

target_compile_options(net7 PRIVATE
    -Wall
    -Wextra
    -Wno-unused-parameter
    -Wno-unused-variable
    -Wno-sign-compare
    -Wno-reorder
    -Wno-deprecated-declarations
    # The Aux* SetXXX methods take addresses of packed-struct members so
    # the ReplaceData<T*> template can memcpy through them. Suppress the
    # broad-brush warning here; Phase F will narrow it if any genuine
    # alignment issue surfaces.
    -Wno-address-of-packed-member
    # MSVC headers freely emit "char *" pointers for string literals;
    # GCC's ISO mode warns on every such conversion. Suppress until
    # Phase F runs the const-correctness pass.
    -Wno-write-strings
    # -fpermissive: the legacy codebase is full of Windows-isms gcc
    # rejects under strict ISO rules. We accept the laxity during Phase
    # B and tighten it in Phase F.
    -fpermissive
)

target_link_libraries(net7 PRIVATE
    Threads::Threads
    OpenSSL::SSL
    OpenSSL::Crypto
    ${LUA_LIBRARIES}
    ${CRYPTOPP_LIBRARIES}
    dl
)

if(TinyXML_FOUND)
    target_link_libraries(net7 PRIVATE ${TinyXML_LIBRARIES})
    target_include_directories(net7 PRIVATE ${TinyXML_INCLUDE_DIRS})
endif()

# --------------------------------------------------------------------------
# Install
# --------------------------------------------------------------------------
install(TARGETS net7 RUNTIME DESTINATION bin)
