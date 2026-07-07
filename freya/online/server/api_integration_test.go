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
	"net/url"
	"strconv"
	"testing"
)

// newTestAPI builds the production handler and a TLS test server with a
// cookie-jar client, returning the client and base URL. Secure cookies require
// https, which httptest.NewTLSServer provides; server.Client() trusts the cert.
func newTestAPI(t *testing.T, st *Store) (*http.Client, string) {
	t.Helper()
	cfg := Config{StatusStaleSecs: 90}
	api := newAPIServer(st, newStatusCache(st, cfg.StatusStaleSecs), newGalaxyCache(st), cfg)
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

// loginAPI logs an account in over the real handler, asserting 200 + cookie.
func loginAPI(t *testing.T, c *http.Client, base string, acct testAccount) {
	t.Helper()
	resp := apiPost(t, c, base+"/api/login",
		map[string]string{"username": acct.username, "password": acct.password})
	resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		t.Fatalf("login(%s) = %d, want 200", acct.username, resp.StatusCode)
	}
}

func bootstrapAPI(t *testing.T, c *http.Client, base string) bootstrapResponse {
	t.Helper()
	resp, err := c.Get(base + "/api/bootstrap")
	if err != nil {
		t.Fatalf("GET bootstrap: %v", err)
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		t.Fatalf("bootstrap = %d, want 200", resp.StatusCode)
	}
	var boot bootstrapResponse
	if err := json.NewDecoder(resp.Body).Decode(&boot); err != nil {
		t.Fatalf("decode bootstrap: %v", err)
	}
	return boot
}

// The full SELL -> BUYOUT path over HTTP, exactly as the SPA drives it:
// seller POSTs /api/auction, buyer POSTs /api/auction/{id}/buyout, and the
// buyer's next /api/bootstrap shows the item waiting in the mailbox. This pins
// the routes, the JSON request/response shapes, and the status codes the
// wired-up frontend now depends on.
func TestIT_API_PostThenBuyout(t *testing.T) {
	st, ctx := testStore(t)
	seller := seedAccount(t, st, ctx, 30, 1, 1_000_000)
	buyer := seedAccount(t, st, ctx, 31, 1, 1_000_000)
	itemID, _ := pickTradableItem(t, st, ctx, true)
	seedVaultItem(t, st, ctx, seller.avatars[0].id, 0, itemID, 1, nil)

	sc, base := newTestAPI(t, st)
	loginAPI(t, sc, base, seller)

	// Seller lists the item. The response is the MyListingView the SPA renders.
	resp := apiPost(t, sc, base+"/api/auction", map[string]any{
		"itemId": strconv.Itoa(itemID), "slot": 0, "avatar": seller.avatars[0].name,
		"stack": 1, "duration": "short", "minBid": 100, "buyout": 5000,
	})
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		t.Fatalf("post listing = %d, want 200", resp.StatusCode)
	}
	var listing MyListingView
	if err := json.NewDecoder(resp.Body).Decode(&listing); err != nil {
		t.Fatalf("decode listing: %v", err)
	}
	if listing.ID == "" {
		t.Fatal("posted listing has empty id")
	}

	// A fresh client logs in as the buyer (separate cookie jar) and buys it out.
	bc, _ := newTestAPI(t, st)
	loginAPI(t, bc, base, buyer)
	// Buyout charges the acting topbar character (PB-31), so the body names it --
	// exactly as the SPA's api.buyout(listingId, avatar) does.
	bresp := apiPost(t, bc, base+"/api/auction/"+listing.ID+"/buyout",
		map[string]any{"avatar": buyer.avatars[0].name})
	bresp.Body.Close()
	if bresp.StatusCode != http.StatusOK {
		t.Fatalf("buyout = %d, want 200", bresp.StatusCode)
	}

	// The buyer's mailbox now carries the item; the listing is gone from browse.
	boot := bootstrapAPI(t, bc, base)
	if !hasItemAttachment(boot.Mail, itemID) {
		t.Errorf("buyer mailbox missing bought item; mail=%+v", boot.Mail)
	}
	for _, l := range boot.Listings {
		if l.ID == listing.ID {
			t.Errorf("bought-out listing %s still in browse", listing.ID)
		}
	}
}

// The MAIL read + loot path over HTTP: deliver a credits mail, mark it read,
// loot the attachment, and confirm the wallet grew on the next bootstrap.
func TestIT_API_MailReadAndLoot(t *testing.T) {
	st, ctx := testStore(t)
	acct := seedAccount(t, st, ctx, 32, 1, 1000)
	deliverCreditsForTest(t, st, ctx, acct.id, acct.avatars[0].id, 777)

	c, base := newTestAPI(t, st)
	loginAPI(t, c, base, acct)

	boot := bootstrapAPI(t, c, base)
	var mailID string
	for _, m := range boot.Mail {
		if hasCreditsAttachment([]MailView{m}, 777) {
			mailID = m.ID
		}
	}
	if mailID == "" {
		t.Fatalf("credit mail not present in bootstrap; mail=%+v", boot.Mail)
	}

	rresp := apiPost(t, c, base+"/api/mail/"+mailID+"/read", map[string]any{})
	rresp.Body.Close()
	if rresp.StatusCode != http.StatusOK {
		t.Fatalf("mark read = %d, want 200", rresp.StatusCode)
	}

	lresp := apiPost(t, c, base+"/api/mail/"+mailID+"/loot", map[string]any{"index": 0})
	lresp.Body.Close()
	if lresp.StatusCode != http.StatusOK {
		t.Fatalf("loot = %d, want 200", lresp.StatusCode)
	}

	after := bootstrapAPI(t, c, base)
	if after.Session.Credits != 1000+777 {
		t.Errorf("credits after loot = %d, want %d", after.Session.Credits, 1000+777)
	}
}

// The full password-reset path over HTTP, as the Account tab drives it:
//   - unauthenticated -> 401 (cookie is the only identity; no username in body)
//   - wrong current password -> 401, password unchanged
//   - new password shorter than 8 -> 400, password unchanged
//   - correct current + valid new -> 200, and the OLD password no longer logs in
//     while the NEW one does (proving the stored PHC was actually rotated).
func TestIT_API_ChangePassword(t *testing.T) {
	st, ctx := testStore(t)
	acct := seedAccount(t, st, ctx, 40, 1, 1000)
	const newPass = "brand-new-pw-123"

	// Unauthenticated: rejected, no session.
	uc, base := newTestAPI(t, st)
	resp := apiPost(t, uc, base+"/api/account/password",
		map[string]string{"currentPassword": acct.password, "newPassword": newPass})
	resp.Body.Close()
	if resp.StatusCode != http.StatusUnauthorized {
		t.Fatalf("change(unauth) = %d, want 401", resp.StatusCode)
	}

	// Authenticated client for the rest.
	c, _ := newTestAPI(t, st)
	loginAPI(t, c, base, acct)

	// Wrong current password -> 401.
	resp = apiPost(t, c, base+"/api/account/password",
		map[string]string{"currentPassword": "wrong-pw", "newPassword": newPass})
	resp.Body.Close()
	if resp.StatusCode != http.StatusUnauthorized {
		t.Fatalf("change(bad current) = %d, want 401", resp.StatusCode)
	}

	// Too-short new password -> 400.
	resp = apiPost(t, c, base+"/api/account/password",
		map[string]string{"currentPassword": acct.password, "newPassword": "short7!"})
	resp.Body.Close()
	if resp.StatusCode != http.StatusBadRequest {
		t.Fatalf("change(short) = %d, want 400", resp.StatusCode)
	}

	// Correct current + valid new -> 200.
	resp = apiPost(t, c, base+"/api/account/password",
		map[string]string{"currentPassword": acct.password, "newPassword": newPass})
	resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		t.Fatalf("change(good) = %d, want 200", resp.StatusCode)
	}

	// The OLD password must no longer authenticate; the NEW one must. Use fresh
	// cookie jars so each login is judged purely on credentials.
	oldc, _ := newTestAPI(t, st)
	r := apiPost(t, oldc, base+"/api/login",
		map[string]string{"username": acct.username, "password": acct.password})
	r.Body.Close()
	if r.StatusCode != http.StatusUnauthorized {
		t.Errorf("login(old pw) = %d, want 401", r.StatusCode)
	}

	newc, _ := newTestAPI(t, st)
	r = apiPost(t, newc, base+"/api/login",
		map[string]string{"username": acct.username, "password": newPass})
	r.Body.Close()
	if r.StatusCode != http.StatusOK {
		t.Errorf("login(new pw) = %d, want 200", r.StatusCode)
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

// The Profile endpoints over HTTP: unauth is 401, an owned character returns its
// identity card, and another account's character name is 404 (ownership scoped).
func TestIT_API_Profile(t *testing.T) {
	st, ctx := testStore(t)
	owner := seedAccount(t, st, ctx, 50, 1, 4200)
	other := seedAccount(t, st, ctx, 51, 1, 0)
	name := owner.avatars[0].name

	c, base := newTestAPI(t, st)

	// Unauthenticated -> 401.
	resp, err := c.Get(base + "/api/avatar/" + url.PathEscape(name) + "/profile")
	if err != nil {
		t.Fatalf("GET profile (unauth): %v", err)
	}
	resp.Body.Close()
	if resp.StatusCode != http.StatusUnauthorized {
		t.Fatalf("unauth profile = %d, want 401", resp.StatusCode)
	}

	loginAPI(t, c, base, owner)

	// Owned -> 200 with the identity card.
	resp, err = c.Get(base + "/api/avatar/" + url.PathEscape(name) + "/profile")
	if err != nil {
		t.Fatalf("GET profile: %v", err)
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		t.Fatalf("profile = %d, want 200", resp.StatusCode)
	}
	var p AvatarProfile
	if err := json.NewDecoder(resp.Body).Decode(&p); err != nil {
		t.Fatalf("decode profile: %v", err)
	}
	if p.Class != "Terran Enforcer" || p.Level != 3 || p.Credits != 4200 {
		t.Errorf("profile = %+v, want class Terran Enforcer, level 3, credits 4200", p)
	}

	// The other account's character is not visible to this session -> 404.
	oname := other.avatars[0].name
	resp2, err := c.Get(base + "/api/avatar/" + url.PathEscape(oname) + "/profile")
	if err != nil {
		t.Fatalf("GET profile (cross): %v", err)
	}
	resp2.Body.Close()
	if resp2.StatusCode != http.StatusNotFound {
		t.Errorf("cross-account profile = %d, want 404", resp2.StatusCode)
	}
}

// The galaxy endpoints are auth-gated, return the static topology, and the live
// occupancy reflects an online avatar's sector.
func TestIT_API_Galaxy(t *testing.T) {
	st, ctx := testStore(t)
	acct := seedAccount(t, st, ctx, 52, 1, 0)

	c, base := newTestAPI(t, st)

	// Unauthenticated -> 401 on both.
	for _, path := range []string{"/api/galaxy", "/api/galaxy/occupancy"} {
		resp, err := c.Get(base + path)
		if err != nil {
			t.Fatalf("GET %s (unauth): %v", path, err)
		}
		resp.Body.Close()
		if resp.StatusCode != http.StatusUnauthorized {
			t.Fatalf("unauth %s = %d, want 401", path, resp.StatusCode)
		}
	}

	loginAPI(t, c, base, acct)

	// Topology.
	resp, err := c.Get(base + "/api/galaxy")
	if err != nil {
		t.Fatalf("GET galaxy: %v", err)
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		t.Fatalf("galaxy = %d, want 200", resp.StatusCode)
	}
	var m GalaxyMap
	if err := json.NewDecoder(resp.Body).Decode(&m); err != nil {
		t.Fatalf("decode galaxy: %v", err)
	}
	if len(m.Systems) == 0 || len(m.Sectors) == 0 || len(m.Edges) == 0 {
		t.Fatalf("thin topology: %d systems, %d sectors, %d edges",
			len(m.Systems), len(m.Sectors), len(m.Edges))
	}

	// Put this account's avatar online in Earth (1060) and confirm occupancy
	// reflects it. The cache is fresh per test server, so the first read is live.
	const earth int64 = 1060
	mustExec(t, st.user, ctx, `UPDATE avatar_info SET sector = $1 WHERE avatar_id = $2`, earth, acct.avatars[0].id)
	setAccountOnline(t, st, ctx, acct.id)
	setAvatarOnline(t, st, ctx, acct.avatars[0].id)

	resp2, err := c.Get(base + "/api/galaxy/occupancy")
	if err != nil {
		t.Fatalf("GET occupancy: %v", err)
	}
	defer resp2.Body.Close()
	if resp2.StatusCode != http.StatusOK {
		t.Fatalf("occupancy = %d, want 200", resp2.StatusCode)
	}
	var occ GalaxyOccupancy
	if err := json.NewDecoder(resp2.Body).Decode(&occ); err != nil {
		t.Fatalf("decode occupancy: %v", err)
	}
	if occ.Counts[itoa(earth)] < 1 {
		t.Errorf("Earth occupancy = %d, want >= 1 (our online avatar)", occ.Counts[itoa(earth)])
	}
}
