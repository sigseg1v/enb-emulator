# tests/CMakeLists.txt
#
# GoogleTest harness for the Net-7 server. Phase G adds a protocol layout
# test and a (env-gated) Postgres smoke test; real subsystem tests grow
# from here.

cmake_minimum_required(VERSION 3.20)
project(net7-tests CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

include(FetchContent)

# Pin to a recent stable tag. Update with intent, not casually.
FetchContent_Declare(
    googletest
    GIT_REPOSITORY https://github.com/google/googletest.git
    GIT_TAG        v1.15.2
)

set(gtest_force_shared_crt ON CACHE BOOL "" FORCE)
FetchContent_MakeAvailable(googletest)

enable_testing()

# --- Smoke: GTest/CTest wiring -------------------------------------------
add_executable(smoke_test
    smoke_test.cpp
)
target_link_libraries(smoke_test PRIVATE GTest::gtest_main)

# --- Protocol: wire-layout pins for the packet headers -------------------
add_executable(header_layout_test
    protocol/header_layout_test.cpp
)
target_link_libraries(header_layout_test PRIVATE GTest::gtest_main)

# --- DB: live Postgres smoke (env-gated) ---------------------------------
# Built only if libpq is available; the test itself self-skips at runtime
# when NET7_TEST_DB_DSN is unset, so it's safe to keep enabled by default.
find_package(PkgConfig QUIET)
set(_NET7_HAS_LIBPQ FALSE)
if(PkgConfig_FOUND)
    pkg_check_modules(LIBPQ IMPORTED_TARGET libpq)
    if(LIBPQ_FOUND)
        set(_NET7_HAS_LIBPQ TRUE)
    endif()
endif()
if(NOT _NET7_HAS_LIBPQ)
    find_library(LIBPQ_LIBRARY NAMES pq)
    find_path(LIBPQ_INCLUDE NAMES libpq-fe.h)
    if(LIBPQ_LIBRARY AND LIBPQ_INCLUDE)
        set(_NET7_HAS_LIBPQ TRUE)
    endif()
endif()

if(_NET7_HAS_LIBPQ)
    add_executable(postgres_smoke_test
        db/postgres_smoke_test.cpp
    )
    target_link_libraries(postgres_smoke_test PRIVATE GTest::gtest_main)
    if(TARGET PkgConfig::LIBPQ)
        target_link_libraries(postgres_smoke_test PRIVATE PkgConfig::LIBPQ)
    else()
        target_include_directories(postgres_smoke_test PRIVATE ${LIBPQ_INCLUDE})
        target_link_libraries(postgres_smoke_test PRIVATE ${LIBPQ_LIBRARY})
    endif()
    include(GoogleTest)
    gtest_discover_tests(postgres_smoke_test)
else()
    message(STATUS "net7-tests: libpq not found — skipping postgres_smoke_test")
endif()

# --- Client: Westwood RSA/RC4 + capture parser ---------------------------
# The standalone test client lives at tests/client/. Built only if OpenSSL
# is available (Westwood RSA leans on libcrypto BIGNUM ops).
find_package(OpenSSL QUIET)
if(OpenSSL_FOUND)
    add_executable(handshake_test
        client/handshake_test.cpp
        client/capture_parser.cpp
        client/westwood/westwood_rsa.cpp
        client/westwood/westwood_rc4.cpp
    )
    target_include_directories(handshake_test PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/client
    )
    target_compile_definitions(handshake_test PRIVATE
        NET7_TEST_CAPTURES_DIR=\"${CMAKE_CURRENT_SOURCE_DIR}/client/captures\"
    )
    target_link_libraries(handshake_test PRIVATE
        GTest::gtest_main
        OpenSSL::Crypto
    )
    include(GoogleTest)
    gtest_discover_tests(handshake_test)
else()
    message(STATUS "net7-tests: OpenSSL not found — skipping handshake_test")
endif()

include(GoogleTest)
gtest_discover_tests(smoke_test)
gtest_discover_tests(header_layout_test)
