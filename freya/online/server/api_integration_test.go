// SPDX-License-Identifier: MIT
// Freya Online -- API-level integration tests.
//
// Drives the real HTTP handler the way main.go composes it
// (session.LoadAndSave(routes())) over a TLS httptest server, with a cookie jar,
// so the scs session cookie (HttpOnly + Secure) round-trips exactly as a browser
// would. Proves the real /api/login -> /api/bootstrap path returns real data and
// that the offline-guard surfaces as HTTP 409. Gated on FREYA_TEST_DB.

package main

import (
	"bytes"
	"encoding/json"
	"net/http"
	"net/http/cookiejar"
	"net/http/httptest"
	"strconv"
	"testing"
)

// newTestAPI builds the production handler and a TLS test server with a
// cookie-jar client, returning the client and base URL. Secure cookies require
// https, which httptest.NewTLSServer provides; server.Client() trusts the cert.
func newTestAPI(t *testing.T, st *Store) (*http.Client, string) {
	t.Helper()
	cfg := Config{StatusStaleSecs: 90}
	api := newAPIServer(st, newStatusCache(st, cfg.StatusStaleSecs), cfg)
	handler := api.session.LoadAndSave(api.routes())
	srv := httptest.NewTLSServer(handler)
	t.Cleanup(srv.Close)

	jar, err := cookiejar.New(nil)
	if err != nil {
		t.Fatalf("cookiejar: %v", err)
	}
	c := srv.Client()
	c.Jar = jar
	return c, srv.URL
}

func apiPost(t *testing.T, c *http.Client, url string, body any) *http.Response {
	t.Helper()
	b, _ := json.Marshal(body)
	resp, err := c.Post(url, "application/json", bytes.NewReader(b))
	if err != nil {
		t.Fatalf("POST %s: %v", url, err)
	}
	return resp
}

// The headline path: log in over the real handler, then bootstrap and get real
// account data (characters, summed credits, the mailbox, browse listings, vault).
func TestIT_API_LoginThenBootstrap(t *testing.T) {
	st, ctx := testStore(t)
	acct := seedAccount(t, st, ctx, 20, 2, 2500) // 2 chars @ 2500

	// Give one character a tradable vault item so the vault payload is non-empty.
	itemID, _ := pickTradableItem(t, st, ctx, true)
	seedVaultItem(t, st, ctx, acct.avatars[0].id, 0, itemID, 3, nil)

	c, base := newTestAPI(t, st)

	// Wrong password -> 401, no session.
	resp := apiPost(t, c, base+"/api/login",
		map[string]string{"username": acct.username, "password": "nope"})
	resp.Body.Close()
	if resp.StatusCode != http.StatusUnauthorized {
		t.Fatalf("login(bad) status = %d, want 401", resp.StatusCode)
	}

	// Correct password -> 200 + session cookie in the jar.
	resp = apiPost(t, c, base+"/api/login",
		map[string]string{"username": acct.username, "password": acct.password})
	resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		t.Fatalf("login(good) status = %d, want 200", resp.StatusCode)
	}

	// Bootstrap returns the authenticated payload.
	bresp, err := c.Get(base + "/api/bootstrap")
	if err != nil {
		t.Fatalf("GET bootstrap: %v", err)
	}
	defer bresp.Body.Close()
	if bresp.StatusCode != http.StatusOK {
		t.Fatalf("bootstrap status = %d, want 200", bresp.StatusCode)
	}
	var boot bootstrapResponse
	if err := json.NewDecoder(bresp.Body).Decode(&boot); err != nil {
		t.Fatalf("decode bootstrap: %v", err)
	}
	if boot.Session.Username != acct.username {
		t.Errorf("session.username = %q, want %q", boot.Session.Username, acct.username)
	}
	if len(boot.Session.Characters) != 2 {
		t.Errorf("characters = %d, want 2", len(boot.Session.Characters))
	}
	if boot.Session.Credits != 5000 {
		t.Errorf("credits = %d, want 5000", boot.Session.Credits)
	}
	// The vault for the item-holding character should carry the seeded slot.
	found := false
	for _, slots := range boot.Vault {
		for _, s := range slots {
			if s.Item.ID == strconv.Itoa(itemID) {
				found = true
			}
		}
	}
	if !found {
		t.Errorf("seeded vault item %d absent from bootstrap vault %+v", itemID, boot.Vault)
	}
}

// An unauthenticated bootstrap is 401; status is public.
func TestIT_API_AuthGate(t *testing.T) {
	st, _ := testStore(t)
	c, base := newTestAPI(t, st)

	resp, err := c.Get(base + "/api/bootstrap")
	if err != nil {
		t.Fatalf("GET bootstrap: %v", err)
	}
	resp.Body.Close()
	if resp.StatusCode != http.StatusUnauthorized {
		t.Errorf("unauth bootstrap = %d, want 401", resp.StatusCode)
	}

	sresp, err := c.Get(base + "/api/status")
	if err != nil {
		t.Fatalf("GET status: %v", err)
	}
	sresp.Body.Close()
	if sresp.StatusCode != http.StatusOK {
		t.Errorf("status = %d, want 200 (public)", sresp.StatusCode)
	}
}

// The offline-guard surfaces over HTTP as 409 Conflict when listing an online
// character's item.
func TestIT_API_OfflineGuard409(t *testing.T) {
	st, ctx := testStore(t)
	acct := seedAccount(t, st, ctx, 21, 1, 1_000_000)
	av := acct.avatars[0]
	itemID, _ := pickTradableItem(t, st, ctx, true)
	seedVaultItem(t, st, ctx, av.id, 0, itemID, 1, nil)
	setAvatarOnline(t, st, ctx, av.id)

	c, base := newTestAPI(t, st)
	resp := apiPost(t, c, base+"/api/login",
		map[string]string{"username": acct.username, "password": acct.password})
	resp.Body.Close()

	lresp := apiPost(t, c, base+"/api/auction", map[string]any{
		"itemId": strconv.Itoa(itemID), "slot": 0, "avatar": av.name,
		"stack": 1, "duration": "short", "minBid": 10, "buyout": 0,
	})
	lresp.Body.Close()
	if lresp.StatusCode != http.StatusConflict {
		t.Errorf("post listing (online char) = %d, want 409", lresp.StatusCode)
	}
}
