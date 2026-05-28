# proxy/CMakeLists.txt
#
# net7proxy builds for two targets that share the same source tree:
#
#   * Linux (native g++) — runs server-side as a docker service alongside
#     the game server. Default build target.
#   * Win32 PE (x86_64-w64-mingw32-g++) — runs client-side inside the same
#     WINE prefix as the EnB client. The launcher spawns it as a sibling
#     of the client. Select via the toolchain file at
#     cmake/mingw-w64-x86_64.toolchain.cmake. OpenSSL is statically linked
#     from third_party/openssl-mingw64/.
#
# The `WIN32` preprocessor symbol guards the dead 2010-era client-launcher
# branches and is NEVER defined for either build; both compile the
# "modern" (formerly Linux-only) branch. Platform-specific differences
# inside those branches go through `_WIN32` (intrinsic to MinGW) and the
# shims in Net7.h.

cmake_minimum_required(VERSION 3.20)

project(net7proxy
    VERSION 0.1.0
    DESCRIPTION "Earth & Beyond / Net-7 TCP/UDP proxy (preservation fork)"
    LANGUAGES CXX
)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS ON)
set(CMAKE_EXPORT_COMPILE_COMMANDS ON)

if(NOT CMAKE_BUILD_TYPE AND NOT CMAKE_CONFIGURATION_TYPES)
    set(CMAKE_BUILD_TYPE Debug CACHE STRING "Build type" FORCE)
endif()

# --------------------------------------------------------------------------
# Source discovery
# --------------------------------------------------------------------------
file(GLOB PROXY_SRC CONFIGURE_DEPENDS
    "${CMAKE_CURRENT_SOURCE_DIR}/*.cpp"
)

list(LENGTH PROXY_SRC _proxy_src_count)
message(STATUS "net7proxy: discovered ${_proxy_src_count} .cpp files under proxy/")

# --------------------------------------------------------------------------
# Dependencies
# --------------------------------------------------------------------------
if(WIN32)
    # Cross-compile to MinGW PE: use the statically-built OpenSSL under
    # third_party/openssl-mingw64/. Don't go through find_package — point
    # at the local prefix directly so we don't accidentally pick up the
    # host's libssl.
    set(OPENSSL_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/third_party/openssl-mingw64")
    if(NOT EXISTS "${OPENSSL_ROOT}/lib64/libssl.a")
        message(FATAL_ERROR
            "net7proxy (Win32): expected static OpenSSL at "
            "${OPENSSL_ROOT}/lib64/libssl.a — build it first "
            "(see proxy/third_party/openssl-mingw64/README or "
            "rerun the OpenSSL 3.0.16 mingw64 configure under "
            "proxy/third_party/openssl-3.0.16/).")
    endif()
    set(OPENSSL_INCLUDE_DIRS "${OPENSSL_ROOT}/include")
    set(OPENSSL_LIBRARIES "${OPENSSL_ROOT}/lib64/libssl.a"
                          "${OPENSSL_ROOT}/lib64/libcrypto.a")
else()
    find_package(OpenSSL 3.0 REQUIRED)
    find_package(Threads REQUIRED)
endif()

# --------------------------------------------------------------------------
# Target
# --------------------------------------------------------------------------
add_executable(net7proxy
    ${PROXY_SRC}
)

target_include_directories(net7proxy PRIVATE
    "${CMAKE_CURRENT_SOURCE_DIR}"
    "${CMAKE_CURRENT_SOURCE_DIR}/../common/include"
)

target_compile_definitions(net7proxy PRIVATE
    # OpenSSL 3.x — surfaces any remaining 1.x-only API uses.
    OPENSSL_API_COMPAT=0x30000000L
)

if(WIN32)
    target_include_directories(net7proxy PRIVATE ${OPENSSL_INCLUDE_DIRS})
    target_compile_definitions(net7proxy PRIVATE
        _WIN32_WINNT=0x0601   # Windows 7+ — sufficient for getaddrinfo etc.
        WIN32_LEAN_AND_MEAN
    )
    # CRITICAL: MinGW pre-defines `WIN32`, but in the proxy sources the
    # `WIN32` macro is the discriminator for the dead 2010-era client-
    # launcher code path — none of which compiles or links any more
    # (Phase Q deleted the TCP cluster it talked to, opcode names have
    # been renamed, etc.). Undefine `WIN32` so the maintained "modern"
    # branches compile. Use `_WIN32` (still defined intrinsically by
    # MinGW) for actual platform detection.
    target_compile_options(net7proxy PRIVATE -UWIN32)
else()
    target_compile_definitions(net7proxy PRIVATE
        LINUX
        __linux__
    )
endif()

target_compile_options(net7proxy PRIVATE
    -Wall
    -Wextra
    -Wno-unused-parameter
    -Wno-unused-variable
    -Wno-unused-function
    -Wno-unused-but-set-variable
    -Wno-sign-compare
    -Wno-reorder
    -Wno-deprecated-declarations
    -Wno-address-of-packed-member
    -Wno-write-strings
    -Wno-pointer-arith
    -fpermissive
)

if(WIN32)
    target_link_libraries(net7proxy PRIVATE
        ${OPENSSL_LIBRARIES}
        ws2_32
        crypt32
        iphlpapi
        # winpthreads (MinGW-posix variant) is needed for pthread_*.
        pthread
    )
else()
    target_link_libraries(net7proxy PRIVATE
        Threads::Threads
        OpenSSL::SSL
        OpenSSL::Crypto
    )
endif()

# Strip the .exe suffix from being doubled if someone sets it explicitly.
if(WIN32)
    set_target_properties(net7proxy PROPERTIES OUTPUT_NAME "Net7Proxy")
endif()

# --------------------------------------------------------------------------
# Install
# --------------------------------------------------------------------------
install(TARGETS net7proxy RUNTIME DESTINATION bin)
