package main

import "testing"

func TestClassName(t *testing.T) {
	cases := []struct {
		race, prof     int
		wantName, code string
	}{
		{0, 0, "Enforcer", "TE"}, // Terran warrior-slot
		{0, 1, "Trader", "TT"},
		{0, 2, "Scout", "TS"},
		{1, 0, "Defender", "JD"}, // Jenquai warrior-slot -- NOT "JW"
		{1, 1, "Seeker", "JS"},
		{1, 2, "Explorer", "JE"},
		{2, 0, "Warrior", "PW"},
		{2, 1, "Privateer", "PP"},
		{2, 2, "Sentinel", "PS"},
	}
	for _, c := range cases {
		name, code := className(c.race, c.prof)
		if name != c.wantName || code != c.code {
			t.Errorf("className(%d,%d) = (%q,%q); want (%q,%q)",
				c.race, c.prof, name, code, c.wantName, c.code)
		}
	}
}

func TestClassNameOutOfRange(t *testing.T) {
	if name, code := className(9, 9); code != "??" || name == "" {
		t.Errorf("out-of-range className did not fall back: (%q,%q)", name, code)
	}
}

func TestRenderPlayerLine(t *testing.T) {
	sectors := map[int64]string{42: "Earth"}

	// Entered avatar with a known sector.
	p := onlinePlayer{username: "acct", firstName: "Veretjd", sector: 42,
		race: 1, prof: 2, combat: 5, explore: 12, trade: 3}
	got := renderPlayerLine(p, sectors)
	want := "• **Veretjd** -- Explorer JE, lvl 5/12/3 (C/E/T) -- Earth (42)"
	if got != want {
		t.Errorf("entered player:\n got %q\nwant %q", got, want)
	}

	// Sector id with no name falls back to the bare id.
	p.sector = 99
	got = renderPlayerLine(p, sectors)
	if want := "• **Veretjd** -- Explorer JE, lvl 5/12/3 (C/E/T) -- 99"; got != want {
		t.Errorf("unknown sector:\n got %q\nwant %q", got, want)
	}

	// A session still in character select (no entered avatar).
	cs := onlinePlayer{username: "acct"}
	got = renderPlayerLine(cs, sectors)
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
