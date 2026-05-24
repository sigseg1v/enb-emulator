# proxy/CMakeLists.txt
#
# Phase J: Linux port of Net7Proxy (server-side TCP listener for 3801).
#
# The 9.5K LOC of 2010 Windows-era client-launcher code under proxy/
# is being ported to Linux. The goal of THIS Phase J pass is the
# minimum needed to bind TCP 3801 and accept connections — heavy
# client-side machinery (UDP proxying, opcode dispatch, RC4-encrypted
# sessions, etc.) is wrapped in `#ifdef WIN32` at source level and
# compiles to nothing on Linux.

cmake_minimum_required(VERSION 3.20)

project(net7proxy
    VERSION 0.1.0
    DESCRIPTION "Earth & Beyond / Net-7 server-side TCP proxy (preservation fork)"
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

# Phase M: proxy has no AF_UNIX IPC of its own; the local compat/ tree is
# header-only after the threading_shim deletion that follows.
set(PROXY_COMPAT_SRC "")

list(LENGTH PROXY_SRC _proxy_src_count)
message(STATUS "net7proxy: discovered ${_proxy_src_count} .cpp files under proxy/")

# --------------------------------------------------------------------------
# Dependencies
# --------------------------------------------------------------------------
find_package(OpenSSL 3.0 REQUIRED)
find_package(Threads REQUIRED)

# --------------------------------------------------------------------------
# Target
# --------------------------------------------------------------------------
add_executable(net7proxy
    ${PROXY_SRC}
    ${PROXY_COMPAT_SRC}
)

target_include_directories(net7proxy PRIVATE
    "${CMAKE_CURRENT_SOURCE_DIR}"
    "${CMAKE_CURRENT_SOURCE_DIR}/../common/include"
)

target_compile_definitions(net7proxy PRIVATE
    LINUX
    __linux__
    # OpenSSL 3.x — surfaces any remaining 1.x-only API uses.
    OPENSSL_API_COMPAT=0x30000000L
)

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

target_link_libraries(net7proxy PRIVATE
    Threads::Threads
    OpenSSL::SSL
    OpenSSL::Crypto
)

# --------------------------------------------------------------------------
# Install
# --------------------------------------------------------------------------
install(TARGETS net7proxy RUNTIME DESTINATION bin)
