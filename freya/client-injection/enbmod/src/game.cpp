#include "game.h"

namespace enb { namespace game {
Offsets& offs() {
    static Offsets g;
    return g;
}
}}
