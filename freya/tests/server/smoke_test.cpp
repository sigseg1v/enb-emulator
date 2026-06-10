// tests/smoke_test.cpp
//
// Phase B / Phase G scaffolding. This single test exists to prove the
// CMake + GoogleTest + CTest wiring works end-to-end. Real tests land
// under tests/<area>/ in Phase G.

#include <gtest/gtest.h>

TEST(Smoke, Compiles) {
    EXPECT_EQ(1 + 1, 2);
}
