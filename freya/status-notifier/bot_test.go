package main

import "testing"

func TestClassCode(t *testing.T) {
	cases := []struct {
		race, prof int
		code       string
	}{
		{0, 0, "TE"}, // Terran warrior-slot
		{0, 1, "TT"},
		{0, 2, "TS"},
		{1, 0, "JD"}, // Jenquai warrior-slot -- NOT "JW"
		{1, 1, "JS"},
		{1, 2, "JE"},
		{2, 0, "PW"},
		{2, 1, "PP"},
		{2, 2, "PS"},
	}
	for _, c := range cases {
		if code := classCode(c.race, c.prof); code != c.code {
			t.Errorf("classCode(%d,%d) = %q; want %q", c.race, c.prof, code, c.code)
		}
	}
}

func TestClassCodeOutOfRange(t *testing.T) {
	if code := classCode(9, 9); code != "??" {
		t.Errorf("out-of-range classCode did not fall back: %q", code)
	}
}

func TestRenderPlayerLine(t *testing.T) {
	// The map mixes sector ids (in space) and starbase interior ids (docked) --
	// renderPlayerLine treats both uniformly as id -> name.
	locations := map[int64]string{42: "Earth", 10521: "Nishino Research Facility"}

	// Entered avatar in a known sector (in space). Displayed level is the max of
	// the three skill levels (12 here); the breakdown follows in parens.
	p := onlinePlayer{username: "acct", firstName: "Veretjd", sector: 42,
		race: 1, prof: 2, combat: 5, explore: 12, trade: 3}
	got := renderPlayerLine(p, locations)
	want := "• **Veretjd** -- JE, lvl 12 (C5/E12/T3) -- Earth"
	if got != want {
		t.Errorf("entered player:\n got %q\nwant %q", got, want)
	}

	// Docked: avatar_info.sector holds a starbase interior id, resolved by name.
	p.sector = 10521
	got = renderPlayerLine(p, locations)
	if want := "• **Veretjd** -- JE, lvl 12 (C5/E12/T3) -- Nishino Research Facility"; got != want {
		t.Errorf("docked player:\n got %q\nwant %q", got, want)
	}

	// An id with no name falls back to the bare number.
	p.sector = 99
	got = renderPlayerLine(p, locations)
	if want := "• **Veretjd** -- JE, lvl 12 (C5/E12/T3) -- 99"; got != want {
		t.Errorf("unknown location:\n got %q\nwant %q", got, want)
	}

	// A session still in character select (no entered avatar).
	cs := onlinePlayer{username: "acct"}
	got = renderPlayerLine(cs, locations)
	if want := "• `acct` -- _in character select_"; got != want {
		t.Errorf("char-select:\n got %q\nwant %q", got, want)
	}
}

func TestHumanDuration(t *testing.T) {
	cases := []struct {
		secs int64
		want string
	}{
		{0, "0m"},
		{59, "0m"},
		{60, "1m"},
		{3600, "1h"},
		{3661, "1h 1m"},
		{90061, "1d 1h 1m"},
		{-5, "0m"},
	}
	for _, c := range cases {
		if got := humanDuration(c.secs); got != c.want {
			t.Errorf("humanDuration(%d) = %q; want %q", c.secs, got, c.want)
		}
	}
}

func TestSwapDBName(t *testing.T) {
	in := "postgres://net7:pw@postgres:5432/net7_user?sslmode=disable"
	got := swapDBName(in, "net7")
	want := "postgres://net7:pw@postgres:5432/net7?sslmode=disable"
	if got != want {
		t.Errorf("swapDBName:\n got %q\nwant %q", got, want)
	}
}
