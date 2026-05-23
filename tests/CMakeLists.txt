# tests/CMakeLists.txt
#
# GoogleTest harness for the Net-7 server. Phase G grows this into a
# real test suite; right now it's a single smoke test that exists so the
# CMake/CTest plumbing is in place.

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

# On Linux we don't want gmock to pull in extra junk; default options are fine.
set(gtest_force_shared_crt ON CACHE BOOL "" FORCE)
FetchContent_MakeAvailable(googletest)

enable_testing()

add_executable(smoke_test
    smoke_test.cpp
)
target_link_libraries(smoke_test PRIVATE GTest::gtest_main)

include(GoogleTest)
gtest_discover_tests(smoke_test)
