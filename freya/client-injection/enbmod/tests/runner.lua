-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- runner.lua -- minimal spec runner for the enbmod Lua mod tests.
-- Usage: lua runner.lua spec/<name>_spec.lua
-- Each spec file runs in its OWN Lua process (run_tests.sh loops), so
-- package.loaded never leaks state between specs.

local spec_file = assert(arg[1], "usage: lua runner.lua <spec_file>")

local results = { pass = 0, fail = 0, failures = {} }
local current_test = "?"

-- global API for specs ---------------------------------------------------------
function test(name, fn)
    current_test = name
    local ok, err = pcall(fn)
    if ok then
        results.pass = results.pass + 1
        io.write("  ok    ", name, "\n")
    else
        results.fail = results.fail + 1
        results.failures[#results.failures + 1] = name .. ": " .. tostring(err)
        io.write("  FAIL  ", name, "\n        ", tostring(err), "\n")
    end
end

local function fmt(v)
    if type(v) == "number" and v == math.floor(v) and math.abs(v) > 255 then
        return string.format("%d (0x%X)", v, v)
    end
    return tostring(v)
end

function eq(got, want, label)
    if got ~= want then
        error(string.format("%sexpected %s, got %s",
            label and (label .. ": ") or "", fmt(want), fmt(got)), 2)
    end
end

function ok(cond, label)
    if not cond then error((label or "condition") .. " is falsy", 2) end
end

function near(got, want, tol, label)
    if math.abs(got - want) > (tol or 1) then
        error(string.format("%sexpected ~%s (+/-%s), got %s",
            label and (label .. ": ") or "", fmt(want), tol or 1, fmt(got)), 2)
    end
end

-- run ---------------------------------------------------------------------------
io.write("== ", spec_file, " ==\n")
local chunk, lerr = loadfile(spec_file)
if not chunk then
    io.write("  FAIL  (load error) ", tostring(lerr), "\n")
    os.exit(1)
end
local rok, rerr = pcall(chunk)
if not rok then
    io.write("  FAIL  (spec body error in/after test '", current_test, "') ",
             tostring(rerr), "\n")
    os.exit(1)
end

io.write(string.format("  -- %d passed, %d failed\n", results.pass, results.fail))
os.exit(results.fail == 0 and 0 or 1)
