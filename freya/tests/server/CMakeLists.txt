# tests/server/CMakeLists.txt
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

set(THREADS_PREFER_PTHREAD_FLAG ON)
find_package(Threads REQUIRED)

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
# Pin the layout against the REAL shared wire structs, not a hand-copied mirror,
# so a future repack/reorder of common/include/net7/PacketStructures.h fails this
# test instead of silently diverging from it.
target_include_directories(header_layout_test PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/../../../common/include
)
target_link_libraries(header_layout_test PRIVATE GTest::gtest_main)

# --- Protocol: credits u64 save-field width (regression guard) -----------
# Pins the producer/consumer width agreement for SAVE_CODE_CREDIT_LEVEL so the
# 4-byte-write / 8-byte-read credits-corruption bug cannot return. Header-only.
add_executable(credit_save_width_test
    protocol/credit_save_width_test.cpp
)
target_include_directories(credit_save_width_test PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/../../../server/src
)
target_compile_options(credit_save_width_test PRIVATE -Wno-unused-function)
target_link_libraries(credit_save_width_test PRIVATE GTest::gtest_main)

# --- Protocol: per-packet auth wrapper accept/drop decision (Phase AH) ----
# Drives the same net7::CheckAuthWrapper the server runtime calls
# (server/src/AuthWrapper.h). Header-only, no sodium/openssl needed.
add_executable(auth_wrapper_test
    protocol/auth_wrapper_test.cpp
)
target_include_directories(auth_wrapper_test PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/../../../server/src
)
target_link_libraries(auth_wrapper_test PRIVATE GTest::gtest_main)

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

# --- DB: sqlplus wrapper round-trip (env-gated; Phase N) --------------
# Compiles the actual wrapper TU into the test binary so we cover the
# libpqxx-backed API end-to-end. Self-skips when NET7_TEST_DB_DSN is unset.
if(PkgConfig_FOUND)
    pkg_check_modules(LIBPQXX IMPORTED_TARGET libpqxx)
endif()
if(LIBPQXX_FOUND)
    add_executable(sqlplus_wrapper_test
        db/sqlplus_wrapper_test.cpp
        ${CMAKE_CURRENT_SOURCE_DIR}/../../../server/src/db/sqlplus.cpp
        ${CMAKE_CURRENT_SOURCE_DIR}/../../../common/Mutex.cpp
        db/wrapper_test_stubs.cpp
    )
    target_include_directories(sqlplus_wrapper_test PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../../../server/src
        ${CMAKE_CURRENT_SOURCE_DIR}/../../../common/include
    )
    target_compile_definitions(sqlplus_wrapper_test PRIVATE LINUX __linux__)
    target_compile_options(sqlplus_wrapper_test PRIVATE
        -Wno-write-strings -fpermissive)
    target_link_libraries(sqlplus_wrapper_test PRIVATE
        GTest::gtest_main PkgConfig::LIBPQXX Threads::Threads)
    gtest_discover_tests(sqlplus_wrapper_test)
else()
    message(STATUS "net7-tests: libpqxx not found — skipping sqlplus_wrapper_test")
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

    # Live + loopback handshake driver. Live half is env-gated via
    # NET7_TEST_PROXY_HOST and self-skips when unset.
    add_executable(handshake_live_test
        client/handshake_live_test.cpp
        client/handshake_driver.cpp
        client/tcp_client.cpp
        client/westwood/westwood_rsa.cpp
        client/westwood/westwood_rc4.cpp
    )
    target_include_directories(handshake_live_test PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/client
    )
    target_link_libraries(handshake_live_test PRIVATE
        GTest::gtest_main
        OpenSSL::Crypto
        Threads::Threads
    )
    gtest_discover_tests(handshake_live_test)

    # Replay engine: offline parse tests + env-gated live replay.
    add_executable(replay_test
        client/replay_test.cpp
        client/replay.cpp
        client/handshake_driver.cpp
        client/tcp_client.cpp
        client/capture_parser.cpp
        client/westwood/westwood_rsa.cpp
        client/westwood/westwood_rc4.cpp
    )
    target_include_directories(replay_test PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/client
    )
    target_compile_definitions(replay_test PRIVATE
        NET7_TEST_CAPTURES_DIR=\"${CMAKE_CURRENT_SOURCE_DIR}/client/captures\"
    )
    target_link_libraries(replay_test PRIVATE
        GTest::gtest_main
        OpenSSL::Crypto
        Threads::Threads
    )
    gtest_discover_tests(replay_test)

    # NOTE: the former master_join_test (Phase K) was removed. It master-joined
    # with a bogus avatar id (no in-memory Player), so the proxy's UDP handoff
    # could never get a 0x2009 confirm and spun through its full ~150s cold-start
    # retry budget before falling back -- no reply could land inside any sane
    # test timeout. The happy-path MasterJoin -> ServerRedirect round-trip is
    # covered end-to-end with a REAL session by the Phase-T xUnit suite
    # (tests/integration/.../Opcodes/MasterJoinTests.cs), which the C++ harness
    # cannot replicate (no login/character/ticket flow). The proxy's MasterJoin
    # parse/decrypt/dispatch + connection-liveness path is still exercised here
    # by replay_test (Replay.LivePostHandshakeReplay replays a real captured
    # Master_Join frame and fails on a clean peer-close).

    # Phase K: VersionRequest dispatch on the global-server port (3805).
    add_executable(version_request_test
        client/version_request_test.cpp
        client/handshake_driver.cpp
        client/tcp_client.cpp
        client/westwood/westwood_rsa.cpp
        client/westwood/westwood_rc4.cpp
    )
    target_include_directories(version_request_test PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/client
    )
    target_link_libraries(version_request_test PRIVATE
        GTest::gtest_main
        OpenSSL::Crypto
        Threads::Threads
    )
    gtest_discover_tests(version_request_test)

    # Phase K: LOGIN (0x0002) dispatch on the sector-server port (3500).
    add_executable(sector_login_test
        client/sector_login_test.cpp
        client/handshake_driver.cpp
        client/tcp_client.cpp
        client/westwood/westwood_rsa.cpp
        client/westwood/westwood_rc4.cpp
    )
    target_include_directories(sector_login_test PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/client
    )
    target_link_libraries(sector_login_test PRIVATE
        GTest::gtest_main
        OpenSSL::Crypto
        Threads::Threads
    )
    gtest_discover_tests(sector_login_test)

    # Phase AH: shared per-peer DTLS transport (proxy<->server UDP leg).
    # Compiles the real common/DtlsTransport.cpp TU and drives a full
    # client<->server handshake + bidirectional app round-trip over its
    # in-memory BIO pump. Needs libssl (DTLS) on top of libcrypto.
    add_executable(dtls_transport_test
        protocol/dtls_transport_test.cpp
        ${CMAKE_CURRENT_SOURCE_DIR}/../../../common/DtlsTransport.cpp
    )
    target_include_directories(dtls_transport_test PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../../../common/include
    )
    target_link_libraries(dtls_transport_test PRIVATE
        GTest::gtest_main
        OpenSSL::SSL
        OpenSSL::Crypto
    )
    gtest_discover_tests(dtls_transport_test)
else()
    message(STATUS "net7-tests: OpenSSL not found — skipping handshake_test")
endif()

include(GoogleTest)
gtest_discover_tests(smoke_test)
gtest_discover_tests(header_layout_test)
gtest_discover_tests(auth_wrapper_test)
