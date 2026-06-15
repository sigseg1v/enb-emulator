// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

package main

import (
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
)

// patcherHashEq is case-insensitive and rejects empty/length-mismatched hashes.
func TestPatcherHashEq(t *testing.T) {
	if !patcherHashEq("ABCdef", "abcDEF") {
		t.Fatal("case-insensitive equal hashes should match")
	}
	if patcherHashEq("", "abc") || patcherHashEq("abc", "") {
		t.Fatal("empty hash must never match")
	}
	if patcherHashEq("abc", "abcd") {
		t.Fatal("length-mismatched hashes must not match")
	}
}

// patcherJSONField pulls a string field out of the launcher's fixed POST body
// and never errors on malformed input (returns "").
func TestPatcherJSONField(t *testing.T) {
	body := `{"launcherHash":"aa11","proxyHash":"bb22","enbmodHash":"cc33"}`
	if v := patcherJSONField(body, "launcherHash"); v != "aa11" {
		t.Fatalf("launcherHash: got %q", v)
	}
	if v := patcherJSONField(body, "enbmodHash"); v != "cc33" {
		t.Fatalf("enbmodHash: got %q", v)
	}
	if v := patcherJSONField(body, "missing"); v != "" {
		t.Fatalf("missing field should be empty, got %q", v)
	}
	if v := patcherJSONField("not json", "launcherHash"); v != "" {
		t.Fatalf("malformed body should yield empty, got %q", v)
	}
}

// isSafeSegment accepts simple names and rejects separators/traversal.
func TestIsSafeSegment(t *testing.T) {
	for _, ok := range []string{"enb-patch.exe", "a.b_c-1", "mod1"} {
		if !isSafeSegment(ok) {
			t.Fatalf("%q should be safe", ok)
		}
	}
	for _, bad := range []string{"", ".", "..", "a/b", "a\\b", "../x", "a b"} {
		if isSafeSegment(bad) {
			t.Fatalf("%q should be rejected", bad)
		}
	}
}

// A cold cache (never loaded) fails closed with 503.
func TestUpdateCheckColdCacheIs503(t *testing.T) {
	m := &patcherManifest{} // not loaded
	req := httptest.NewRequest(http.MethodPost, "/updateCheck", strings.NewReader(`{}`))
	rec := httptest.NewRecorder()
	m.handleUpdateCheck(rec, req)
	if rec.Code != http.StatusServiceUnavailable {
		t.Fatalf("cold cache should be 503, got %d", rec.Code)
	}
}

// A loaded cache emits UP_TO_DATE (with mods+patches) when the launcher's hashes
// match, and the patches/mods arrays are always present.
func TestUpdateCheckUpToDateCarriesModsAndPatches(t *testing.T) {
	m := &patcherManifest{
		dlURL:       "https://dl.example",
		loaded:      true,
		launcherExe: "L", launcherCfg: "C", proxyExe: "P",
		mods:    []manifestModEntry{{Id: "hud", Hash: "abc1234567"}},
		patches: []manifestPatchEntry{{Name: "enb-patch.exe", Sha512: "deadbeef"}},
	}
	body := `{"launcherHash":"L","proxyHash":"P"}`
	req := httptest.NewRequest(http.MethodPost, "/updateCheck", strings.NewReader(body))
	rec := httptest.NewRecorder()
	m.handleUpdateCheck(rec, req)

	if rec.Code != http.StatusOK {
		t.Fatalf("expected 200, got %d", rec.Code)
	}
	out := rec.Body.String()
	if !strings.Contains(out, `"status":"UP_TO_DATE"`) {
		t.Fatalf("expected UP_TO_DATE, got %s", out)
	}
	if !strings.Contains(out, `"url":"https://dl.example/mods/hud-abc1234567.zip"`) {
		t.Fatalf("mods url missing/wrong: %s", out)
	}
	if !strings.Contains(out, `"url":"https://dl.example/patches/enb-patch.exe"`) {
		t.Fatalf("patches url missing/wrong: %s", out)
	}
}

// A launcher hash mismatch yields UPDATE_NEEDED with the file list, still
// carrying the mods/patches arrays.
func TestUpdateCheckUpdateNeeded(t *testing.T) {
	m := &patcherManifest{
		dlURL:       "https://dl.example",
		loaded:      true,
		launcherExe: "Lnew", launcherCfg: "Cnew", proxyExe: "P",
	}
	body := `{"launcherHash":"Lold","proxyHash":"P"}`
	req := httptest.NewRequest(http.MethodPost, "/updateCheck", strings.NewReader(body))
	rec := httptest.NewRecorder()
	m.handleUpdateCheck(rec, req)

	out := rec.Body.String()
	if !strings.Contains(out, `"status":"UPDATE_NEEDED"`) {
		t.Fatalf("expected UPDATE_NEEDED, got %s", out)
	}
	if !strings.Contains(out, "FreyaLauncher.exe") || !strings.Contains(out, "FreyaLauncher.cfg") {
		t.Fatalf("launcher mismatch should list exe+cfg: %s", out)
	}
	if !strings.Contains(out, `"patches":[]`) {
		t.Fatalf("empty patches array should still be present: %s", out)
	}
}
