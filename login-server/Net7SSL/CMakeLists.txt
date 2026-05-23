# login-server/Net7SSL/CMakeLists.txt
#
# Phase J: Linux port of Net7SSL (server-side SSL authentication listener).
#
# The 6.7K LOC of 2010 Windows-era login-server code under login-server/Net7SSL/
# is being ported to Linux. The goal of THIS Phase J pass is the minimum needed
# to bind TCP SSL_PORT (443) and accept SSL connections — heavy machinery
# (AccountManager, ClientToGlobalServer opcode dispatch, MySQL ticket store,
# UDPClient, mailslot-based IPC, Connection_B blocking-TCP machinery, etc.)
# is wrapped in `#ifdef WIN32` at source level and compiles to nothing on
# Linux.
#
# Modelled on proxy/CMakeLists.txt.

cmake_minimum_required(VERSION 3.20)

project(net7ssl
    VERSION 0.1.0
    DESCRIPTION "Earth & Beyond / Net-7 SSL authentication server (preservation fork)"
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
file(GLOB NET7SSL_SRC CONFIGURE_DEPENDS
    "${CMAKE_CURRENT_SOURCE_DIR}/*.cpp"
)

file(GLOB NET7SSL_COMPAT_SRC CONFIGURE_DEPENDS
    "${CMAKE_CURRENT_SOURCE_DIR}/compat/*.cpp"
)

list(LENGTH NET7SSL_SRC _net7ssl_src_count)
message(STATUS "net7ssl: discovered ${_net7ssl_src_count} .cpp files under login-server/Net7SSL/")

# --------------------------------------------------------------------------
# Dependencies
# --------------------------------------------------------------------------
find_package(OpenSSL 3.0 REQUIRED)
find_package(Threads REQUIRED)

# libmysqlclient is consumed via pkg-config; the upstream package ships
# either `mysqlclient` or (on noble) the unprefixed name. Fall back to
# the linker flag if pkg-config can't find it.
find_package(PkgConfig REQUIRED)
pkg_check_modules(MYSQLCLIENT IMPORTED_TARGET mysqlclient)
if(NOT MYSQLCLIENT_FOUND)
    message(STATUS "net7ssl: pkg-config didn't find mysqlclient; falling back to -lmysqlclient")
endif()

# --------------------------------------------------------------------------
# Target
# --------------------------------------------------------------------------
add_executable(net7ssl
    ${NET7SSL_SRC}
    ${NET7SSL_COMPAT_SRC}
)

target_include_directories(net7ssl PRIVATE
    "${CMAKE_CURRENT_SOURCE_DIR}"
    "${CMAKE_CURRENT_SOURCE_DIR}/compat"
)

target_compile_definitions(net7ssl PRIVATE
    LINUX
    __linux__
    # OpenSSL 3.x — surfaces any remaining 1.x-only API uses.
    OPENSSL_API_COMPAT=0x30000000L
)

target_compile_options(net7ssl PRIVATE
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
    -Wno-format
    -fpermissive
)

target_link_libraries(net7ssl PRIVATE
    Threads::Threads
    OpenSSL::SSL
    OpenSSL::Crypto
)

if(MYSQLCLIENT_FOUND)
    target_link_libraries(net7ssl PRIVATE PkgConfig::MYSQLCLIENT)
else()
    target_link_libraries(net7ssl PRIVATE mysqlclient)
endif()

# --------------------------------------------------------------------------
# Install
# --------------------------------------------------------------------------
install(TARGETS net7ssl RUNTIME DESTINATION bin)
