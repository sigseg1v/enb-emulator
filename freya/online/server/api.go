// SPDX-License-Identifier: MIT
// Freya Online -- website JSON API (/api/*).
//
// This half is NEW (not legacy wire): the React SPA talks to it over fetch with
// credentials. Sessions use github.com/alexedwards/scs/v2 -- a maintained
// session manager -- rather than a hand-rolled cookie, per the project rule to
// not roll our own auth middleware. The session cookie is HttpOnly + Secure +
// SameSite=Lax; the SPA never sees the session token, only the JSON payloads.
//
// The website login authenticates against the SAME accounts table / Argon2id PHC
// as the game (auth.go), but issues a website session -- it does NOT mint a game
// login ticket. The two credential paths are independent on purpose.

package main

import (
	"context"
	"encoding/json"
	"errors"
	"log"
	"net/http"
	"strconv"
	"time"

	"github.com/alexedwards/scs/v2"
)

const (
	sessionKeyUser    = "username"
	sessionKeyAccount = "account_id"
)

type apiServer struct {
	store   *Store
	status  *statusCache
	galaxy  *galaxyCache
	session *scs.SessionManager
	cfg     Config
}

func newAPIServer(store *Store, status *statusCache, galaxy *galaxyCache, cfg Config) *apiServer {
	sm := scs.New()
	sm.Lifetime = 24 * time.Hour
	sm.IdleTimeout = 2 * time.Hour
	sm.Cookie.Name = "freya_session"
	sm.Cookie.HttpOnly = true
	sm.Cookie.SameSite = http.SameSiteLaxMode
	// Persist across browser restarts (scs default), so a refresh keeps the
	// session instead of dropping back to the login form.
	sm.Cookie.Persist = true
	// Secure means the browser only sends the cookie over HTTPS. The live site
	// is TLS, so it must stay set there -- but play-local serves the SPA over
	// plain HTTP (http://localhost:8088), where a Secure cookie is silently
	// dropped and the session never survives a refresh. DOMAIN=localhost is the
	// play-local marker, so drop Secure only there.
	sm.Cookie.Secure = cfg.Domain != "localhost"
	sm.Cookie.Path = "/"
	return &apiServer{store: store, status: status, galaxy: galaxy, session: sm, cfg: cfg}
}

// routes registers the JSON API under /api/. The returned handler must be
// wrapped by s.session.LoadAndSave (done in main) so session state is loaded.
func (s *apiServer) routes() http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc("/api/status", s.handleStatus)
	mux.HandleFunc("/api/login", s.handleLogin)
	mux.HandleFunc("/api/logout", s.handleLogout)
	mux.HandleFunc("/api/session", s.handleSession)
	mux.HandleFunc("/api/bootstrap", s.handleBootstrap)
	// Galaxy map: static topology (cached hard) + live per-sector occupancy
	// (short-cached, polled by the SPA).
	mux.HandleFunc("GET /api/galaxy", s.handleGalaxyMap)
	mux.HandleFunc("GET /api/galaxy/occupancy", s.handleGalaxyOccupancy)
	// Live directed sector-to-sector traffic (last 5 min, grouped by ordered
	// pair); cached 60s. Drives the travelling-light animation on the lanes.
	mux.HandleFunc("GET /api/galaxy/gateflow", s.handleGalaxyGateFlow)
	// The logged-in account's own characters and the sectors they sit in (for
	// personal star markers on the map). Auth-gated -- it is per-account data.
	mux.HandleFunc("GET /api/galaxy/me", s.handleGalaxyMe)
	// AH + mailbox mutations (Go 1.22 method+wildcard patterns).
	mux.HandleFunc("POST /api/auction", s.handlePostListing)
	mux.HandleFunc("POST /api/auction/{id}/bid", s.handleBid)
	mux.HandleFunc("POST /api/auction/{id}/buyout", s.handleBuyout)
	mux.HandleFunc("POST /api/mail", s.handleSendMail)
	mux.HandleFunc("POST /api/mail/{id}/read", s.handleMarkRead)
	mux.HandleFunc("POST /api/mail/{id}/loot", s.handleLoot)
	mux.HandleFunc("DELETE /api/mail/{id}", s.handleDeleteMail)
	// Vault item transfer between two of the account's own characters.
	mux.HandleFunc("POST /api/vault/transfer", s.handleVaultTransfer)
	// Profile reads, split per concern so character-switch loads on demand.
	mux.HandleFunc("GET /api/avatar/{name}/profile", s.handleAvatarProfile)
	mux.HandleFunc("GET /api/avatar/{name}/ship", s.handleAvatarShip)
	mux.HandleFunc("GET /api/avatar/{name}/skills", s.handleAvatarSkills)
	// Account self-service.
	mux.HandleFunc("POST /api/account/password", s.handleChangePassword)
	return mux
}

func writeJSON(w http.ResponseWriter, code int, v any) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(code)
	if err := json.NewEncoder(w).Encode(v); err != nil {
		log.Printf("api: encode: %v", err)
	}
}

// loggedIn returns the session username, or "" if unauthenticated.
func (s *apiServer) loggedIn(r *http.Request) (string, int64) {
	user := s.session.GetString(r.Context(), sessionKeyUser)
	acct := s.session.Get(r.Context(), sessionKeyAccount)
	id, _ := acct.(int64)
	return user, id
}

// GET /api/status -- always-visible server status. Public (no auth).
func (s *apiServer) handleStatus(w http.ResponseWriter, r *http.Request) {
	st := s.status.get(r.Context(), time.Now())
	writeJSON(w, http.StatusOK, st)
}

// POST /api/login {username,password}. Authenticates against accounts/Argon2id.
func (s *apiServer) handleLogin(w http.ResponseWriter, r *http.Request) {
	if r.Method != http.MethodPost {
		writeJSON(w, http.StatusMethodNotAllowed, map[string]string{"error": "method not allowed"})
		return
	}
	var body struct {
		Username string `json:"username"`
		Password string `json:"password"`
	}
	if err := json.NewDecoder(http.MaxBytesReader(w, r.Body, 4096)).Decode(&body); err != nil {
		writeJSON(w, http.StatusBadRequest, map[string]string{"error": "bad request"})
		return
	}
	if body.Username == "" || body.Password == "" {
		writeJSON(w, http.StatusUnauthorized, map[string]string{"error": "invalid credentials"})
		return
	}

	ctx, cancel := context.WithTimeout(r.Context(), 5*time.Second)
	defer cancel()

	phc, err := s.store.passwordPHC(ctx, body.Username)
	if err != nil {
		// Same response whether the account is missing or the password is wrong
		// -- no user enumeration.
		writeJSON(w, http.StatusUnauthorized, map[string]string{"error": "invalid credentials"})
		return
	}
	ok, err := verifyPassword(phc, body.Password)
	if err != nil || !ok {
		writeJSON(w, http.StatusUnauthorized, map[string]string{"error": "invalid credentials"})
		return
	}
	acctID, err := s.store.accountID(ctx, body.Username)
	if err != nil {
		writeJSON(w, http.StatusUnauthorized, map[string]string{"error": "invalid credentials"})
		return
	}

	// Rotate the session token on privilege change (login) -- prevents fixation.
	if err := s.session.RenewToken(r.Context()); err != nil {
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": "session error"})
		return
	}
	s.session.Put(r.Context(), sessionKeyUser, body.Username)
	s.session.Put(r.Context(), sessionKeyAccount, acctID)

	writeJSON(w, http.StatusOK, map[string]any{"username": body.Username})
}

// POST /api/logout -- destroys the session.
func (s *apiServer) handleLogout(w http.ResponseWriter, r *http.Request) {
	if err := s.session.Destroy(r.Context()); err != nil {
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": "session error"})
		return
	}
	writeJSON(w, http.StatusOK, map[string]bool{"ok": true})
}

// GET /api/session -- whoami; {username} or 401.
func (s *apiServer) handleSession(w http.ResponseWriter, r *http.Request) {
	user, _ := s.loggedIn(r)
	if user == "" {
		writeJSON(w, http.StatusUnauthorized, map[string]string{"error": "not authenticated"})
		return
	}
	writeJSON(w, http.StatusOK, map[string]any{"username": user})
}

// sessionView mirrors the SPA Session interface.
type sessionView struct {
	Username   string   `json:"username"`
	Characters []string `json:"characters"`
	// Credits is the account total (kept for any account-wide use); the top bar
	// shows the selected character's balance from CreditsByCharacter.
	Credits            int64            `json:"credits"`
	CreditsByCharacter map[string]int64 `json:"creditsByCharacter"`
}

// bootstrapResponse is the single payload the SPA loads after login. Field
// names/shape match the client's Bootstrap interface (web/src/api.ts).
type bootstrapResponse struct {
	Session  sessionView                `json:"session"`
	Server   ServerStatus               `json:"server"`
	Mail     []MailView                 `json:"mail"`
	Listings []ListingView              `json:"listings"`
	Vault    map[string][]VaultSlotView `json:"vault"`
	// VaultStorage is each character's ACTUAL vault (avatar_vault_items only,
	// including no-trade items) -- the Vault transfer page and the mail composer's
	// item picker render it. Distinct from Vault above (AH-listable: vault +
	// inventory, tradable only).
	VaultStorage map[string][]VaultSlotView `json:"vaultStorage"`
	MyListings   []MyListingView            `json:"myListings"`
}

// GET /api/bootstrap -- the authenticated SPA's initial data load.
func (s *apiServer) handleBootstrap(w http.ResponseWriter, r *http.Request) {
	user, acctID := s.loggedIn(r)
	if user == "" {
		writeJSON(w, http.StatusUnauthorized, map[string]string{"error": "not authenticated"})
		return
	}

	ctx, cancel := context.WithTimeout(r.Context(), 5*time.Second)
	defer cancel()

	chars, err := s.store.liveCharacters(ctx, acctID)
	if err != nil {
		log.Printf("api: liveCharacters(%d): %v", acctID, err)
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": "internal error"})
		return
	}

	names := make([]string, 0, len(chars))
	creditsBy := make(map[string]int64, len(chars))
	var credits int64
	for _, c := range chars {
		names = append(names, c.Name)
		creditsBy[c.Name] = c.Credits
		credits += c.Credits
	}

	resp := bootstrapResponse{
		Session:      sessionView{Username: user, Characters: names, Credits: credits, CreditsByCharacter: creditsBy},
		Server:       s.status.get(ctx, time.Now()),
		Mail:         s.store.accountMail(ctx, acctID),
		Listings:     s.store.openListings(ctx),
		Vault:        s.store.accountVault(ctx, acctID),
		VaultStorage: s.store.accountVaultStorage(ctx, acctID),
		MyListings:   s.store.listingsBySeller(ctx, acctID),
	}
	writeJSON(w, http.StatusOK, resp)
}

// mutationError maps a store error to an HTTP status + client-safe message. The
// domain errors (insufficient credits, listing gone, ...) are user-facing 4xx;
// anything else is a 500 with a generic message and a server-side log.
func mutationError(w http.ResponseWriter, err error) {
	switch {
	case errors.Is(err, errInsufficient):
		writeJSON(w, http.StatusPaymentRequired, map[string]string{"error": "insufficient credits"})
	case errors.Is(err, errOwnListing):
		writeJSON(w, http.StatusConflict, map[string]string{"error": "cannot bid on your own listing"})
	case errors.Is(err, errCharacterOnline):
		writeJSON(w, http.StatusConflict, map[string]string{"error": errCharacterOnline.Error()})
	case errors.Is(err, errListingGone):
		writeJSON(w, http.StatusGone, map[string]string{"error": "listing no longer available"})
	case errors.Is(err, errNoBuyout):
		writeJSON(w, http.StatusBadRequest, map[string]string{"error": "listing has no buyout"})
	case errors.Is(err, errBadInput), errors.Is(err, errItemUntradable), errors.Is(err, errItemNotFound):
		writeJSON(w, http.StatusBadRequest, map[string]string{"error": err.Error()})
	case errors.Is(err, errNoCharacter):
		writeJSON(w, http.StatusBadRequest, map[string]string{"error": "no live character on this account"})
	case errors.Is(err, errMailGone), errors.Is(err, errAttachGone):
		writeJSON(w, http.StatusNotFound, map[string]string{"error": "not found"})
	case errors.Is(err, errAlreadyLooted):
		writeJSON(w, http.StatusConflict, map[string]string{"error": "already looted"})
	case errors.Is(err, errNoVaultSpace):
		writeJSON(w, http.StatusConflict, map[string]string{"error": "vault is full, cannot loot mail"})
	case errors.Is(err, errDestVaultFull):
		writeJSON(w, http.StatusConflict, map[string]string{"error": errDestVaultFull.Error()})
	case errors.Is(err, errSameCharacter), errors.Is(err, errCharacterNotFound),
		errors.Is(err, errBadRecipient), errors.Is(err, errEmptyMail),
		errors.Is(err, errTooManyItems), errors.Is(err, errDupMailSlot),
		errors.Is(err, errNoSubject):
		writeJSON(w, http.StatusBadRequest, map[string]string{"error": err.Error()})
	default:
		log.Printf("api: mutation: %v", err)
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": "internal error"})
	}
}

// pathID parses the {id} path wildcard as an int64.
func pathID(r *http.Request) (int64, bool) {
	id, err := strconv.ParseInt(r.PathValue("id"), 10, 64)
	if err != nil || id <= 0 {
		return 0, false
	}
	return id, true
}

// POST /api/auction -- list an item. Body is PostListingInput.
func (s *apiServer) handlePostListing(w http.ResponseWriter, r *http.Request) {
	_, acctID := s.loggedIn(r)
	if acctID == 0 {
		writeJSON(w, http.StatusUnauthorized, map[string]string{"error": "not authenticated"})
		return
	}
	var body struct {
		ItemID   string `json:"itemId"`
		Slot     int    `json:"slot"`
		Avatar   string `json:"avatar"`
		Stack    int    `json:"stack"`
		Duration string `json:"duration"`
		MinBid   int64  `json:"minBid"`
		Buyout   int64  `json:"buyout"`
	}
	if err := json.NewDecoder(http.MaxBytesReader(w, r.Body, 8192)).Decode(&body); err != nil {
		writeJSON(w, http.StatusBadRequest, map[string]string{"error": "bad request"})
		return
	}
	itemID, err := strconv.Atoi(body.ItemID)
	if err != nil {
		writeJSON(w, http.StatusBadRequest, map[string]string{"error": "bad item id"})
		return
	}

	ctx, cancel := context.WithTimeout(r.Context(), 8*time.Second)
	defer cancel()

	view, err := s.store.PostListing(ctx, acctID, itemID, body.Slot, body.Stack,
		body.Avatar, body.Duration, body.MinBid, body.Buyout)
	if err != nil {
		mutationError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, view)
}

// POST /api/auction/{id}/bid -- bid the next increment.
func (s *apiServer) handleBid(w http.ResponseWriter, r *http.Request) {
	_, acctID := s.loggedIn(r)
	if acctID == 0 {
		writeJSON(w, http.StatusUnauthorized, map[string]string{"error": "not authenticated"})
		return
	}
	id, ok := pathID(r)
	if !ok {
		writeJSON(w, http.StatusBadRequest, map[string]string{"error": "bad listing id"})
		return
	}
	ctx, cancel := context.WithTimeout(r.Context(), 8*time.Second)
	defer cancel()

	view, err := s.store.PlaceBid(ctx, acctID, id)
	if err != nil {
		mutationError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, view)
}

// POST /api/auction/{id}/buyout -- buy outright.
func (s *apiServer) handleBuyout(w http.ResponseWriter, r *http.Request) {
	_, acctID := s.loggedIn(r)
	if acctID == 0 {
		writeJSON(w, http.StatusUnauthorized, map[string]string{"error": "not authenticated"})
		return
	}
	id, ok := pathID(r)
	if !ok {
		writeJSON(w, http.StatusBadRequest, map[string]string{"error": "bad listing id"})
		return
	}
	ctx, cancel := context.WithTimeout(r.Context(), 8*time.Second)
	defer cancel()

	if err := s.store.Buyout(ctx, acctID, id); err != nil {
		mutationError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, map[string]bool{"ok": true})
}

// POST /api/mail/{id}/read -- mark a message read.
func (s *apiServer) handleMarkRead(w http.ResponseWriter, r *http.Request) {
	_, acctID := s.loggedIn(r)
	if acctID == 0 {
		writeJSON(w, http.StatusUnauthorized, map[string]string{"error": "not authenticated"})
		return
	}
	id, ok := pathID(r)
	if !ok {
		writeJSON(w, http.StatusBadRequest, map[string]string{"error": "bad mail id"})
		return
	}
	ctx, cancel := context.WithTimeout(r.Context(), 5*time.Second)
	defer cancel()

	if err := s.store.MarkRead(ctx, acctID, id); err != nil {
		mutationError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, map[string]bool{"ok": true})
}

// DELETE /api/mail/{id} -- discard a message (and its attachments). Unlooted
// attachments are forfeited.
func (s *apiServer) handleDeleteMail(w http.ResponseWriter, r *http.Request) {
	_, acctID := s.loggedIn(r)
	if acctID == 0 {
		writeJSON(w, http.StatusUnauthorized, map[string]string{"error": "not authenticated"})
		return
	}
	id, ok := pathID(r)
	if !ok {
		writeJSON(w, http.StatusBadRequest, map[string]string{"error": "bad mail id"})
		return
	}
	ctx, cancel := context.WithTimeout(r.Context(), 5*time.Second)
	defer cancel()

	if err := s.store.DeleteMessage(ctx, acctID, id); err != nil {
		mutationError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, map[string]bool{"ok": true})
}

// POST /api/mail/{id}/loot -- loot the index-th attachment. Body: {index}.
func (s *apiServer) handleLoot(w http.ResponseWriter, r *http.Request) {
	_, acctID := s.loggedIn(r)
	if acctID == 0 {
		writeJSON(w, http.StatusUnauthorized, map[string]string{"error": "not authenticated"})
		return
	}
	id, ok := pathID(r)
	if !ok {
		writeJSON(w, http.StatusBadRequest, map[string]string{"error": "bad mail id"})
		return
	}
	var body struct {
		Index int `json:"index"`
	}
	if err := json.NewDecoder(http.MaxBytesReader(w, r.Body, 1024)).Decode(&body); err != nil {
		writeJSON(w, http.StatusBadRequest, map[string]string{"error": "bad request"})
		return
	}
	ctx, cancel := context.WithTimeout(r.Context(), 8*time.Second)
	defer cancel()

	if err := s.store.LootAttachment(ctx, acctID, id, body.Index); err != nil {
		mutationError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, map[string]bool{"ok": true})
}

// minPasswordLen is the floor enforced server-side. The SPA mirrors it, but the
// server is the authority -- a short password is rejected here regardless.
const minPasswordLen = 8

// POST /api/account/password {currentPassword,newPassword}. Changes the signed-in
// account's password. The account is taken from the session cookie, never from
// the request body -- you can only change your OWN password. The current password
// must be re-supplied and verified (so a hijacked-but-idle session, or anyone at
// an unlocked browser, cannot silently rotate it), and the new password is hashed
// into the SAME libsodium Argon2id PHC the game login verifies, so the new
// password works for both the website and the game client.
func (s *apiServer) handleChangePassword(w http.ResponseWriter, r *http.Request) {
	_, acctID := s.loggedIn(r)
	if acctID == 0 {
		writeJSON(w, http.StatusUnauthorized, map[string]string{"error": "not authenticated"})
		return
	}
	var body struct {
		CurrentPassword string `json:"currentPassword"`
		NewPassword     string `json:"newPassword"`
	}
	if err := json.NewDecoder(http.MaxBytesReader(w, r.Body, 4096)).Decode(&body); err != nil {
		writeJSON(w, http.StatusBadRequest, map[string]string{"error": "bad request"})
		return
	}
	if len([]rune(body.NewPassword)) < minPasswordLen {
		writeJSON(w, http.StatusBadRequest,
			map[string]string{"error": "new password must be at least 8 characters"})
		return
	}

	ctx, cancel := context.WithTimeout(r.Context(), 8*time.Second)
	defer cancel()

	phc, err := s.store.passwordPHCByID(ctx, acctID)
	if err != nil {
		log.Printf("api: passwordPHCByID(%d): %v", acctID, err)
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": "internal error"})
		return
	}
	ok, err := verifyPassword(phc, body.CurrentPassword)
	if err != nil || !ok {
		writeJSON(w, http.StatusUnauthorized,
			map[string]string{"error": "current password is incorrect"})
		return
	}

	newPHC, err := hashPassword(body.NewPassword)
	if err != nil {
		log.Printf("api: hashPassword: %v", err)
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": "internal error"})
		return
	}
	if err := s.store.updatePassword(ctx, acctID, newPHC); err != nil {
		log.Printf("api: updatePassword(%d): %v", acctID, err)
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": "internal error"})
		return
	}

	// Rotate the session token on a credential change -- a stolen pre-change
	// cookie is useless after this point.
	if err := s.session.RenewToken(r.Context()); err != nil {
		log.Printf("api: RenewToken after password change: %v", err)
	}
	writeJSON(w, http.StatusOK, map[string]bool{"ok": true})
}

// POST /api/vault/transfer {from,to,slot,itemId}. Moves a vault item between two
// of the account's own characters. The account is taken from the session; both
// characters must belong to it, must differ, and must be offline. Dup-safe: the
// move runs in one serializable transaction.
func (s *apiServer) handleVaultTransfer(w http.ResponseWriter, r *http.Request) {
	_, acctID := s.loggedIn(r)
	if acctID == 0 {
		writeJSON(w, http.StatusUnauthorized, map[string]string{"error": "not authenticated"})
		return
	}
	var body struct {
		From   string `json:"from"`
		To     string `json:"to"`
		Slot   int    `json:"slot"`
		ItemID int    `json:"itemId"`
	}
	if err := json.NewDecoder(http.MaxBytesReader(w, r.Body, 4096)).Decode(&body); err != nil {
		writeJSON(w, http.StatusBadRequest, map[string]string{"error": "bad request"})
		return
	}
	if body.From == "" || body.To == "" {
		writeJSON(w, http.StatusBadRequest, map[string]string{"error": "both characters are required"})
		return
	}

	ctx, cancel := context.WithTimeout(r.Context(), 8*time.Second)
	defer cancel()

	if err := s.store.TransferVaultItem(ctx, acctID, body.From, body.To, body.Slot, body.ItemID); err != nil {
		mutationError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, map[string]bool{"ok": true})
}

// POST /api/mail {from,to,subject,body,items}. Composes player-to-player mail
// from one of the account's characters. Items (up to 6) come from the sender's
// vault and are removed atomically. Either a body or at least one item is
// required. The sender is taken (by name) from the account's own characters; the
// recipient may be any live character but not the sender.
func (s *apiServer) handleSendMail(w http.ResponseWriter, r *http.Request) {
	_, acctID := s.loggedIn(r)
	if acctID == 0 {
		writeJSON(w, http.StatusUnauthorized, map[string]string{"error": "not authenticated"})
		return
	}
	var body struct {
		From    string          `json:"from"`
		To      string          `json:"to"`
		Subject string          `json:"subject"`
		Body    string          `json:"body"`
		Items   []MailItemInput `json:"items"`
	}
	if err := json.NewDecoder(http.MaxBytesReader(w, r.Body, 8192)).Decode(&body); err != nil {
		writeJSON(w, http.StatusBadRequest, map[string]string{"error": "bad request"})
		return
	}
	if body.From == "" || body.To == "" {
		writeJSON(w, http.StatusBadRequest, map[string]string{"error": "sender and recipient are required"})
		return
	}

	ctx, cancel := context.WithTimeout(r.Context(), 8*time.Second)
	defer cancel()

	if err := s.store.SendMail(ctx, acctID, body.From, body.To, body.Subject, body.Body, body.Items); err != nil {
		mutationError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, map[string]bool{"ok": true})
}

// avatarName extracts and validates the {name} path wildcard for the Profile
// endpoints. Empty/oversized names are rejected before any DB work.
func avatarName(r *http.Request) (string, bool) {
	n := r.PathValue("name")
	if n == "" || len(n) > 64 {
		return "", false
	}
	return n, true
}

// GET /api/avatar/{name}/profile -- identity card (class, levels, sector).
func (s *apiServer) handleAvatarProfile(w http.ResponseWriter, r *http.Request) {
	_, acctID := s.loggedIn(r)
	if acctID == 0 {
		writeJSON(w, http.StatusUnauthorized, map[string]string{"error": "not authenticated"})
		return
	}
	name, ok := avatarName(r)
	if !ok {
		writeJSON(w, http.StatusBadRequest, map[string]string{"error": "bad character name"})
		return
	}
	ctx, cancel := context.WithTimeout(r.Context(), 5*time.Second)
	defer cancel()
	p, err := s.store.avatarProfile(ctx, acctID, name)
	if err != nil {
		avatarReadError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, p)
}

// GET /api/avatar/{name}/ship -- starship card + fitted equipment.
func (s *apiServer) handleAvatarShip(w http.ResponseWriter, r *http.Request) {
	_, acctID := s.loggedIn(r)
	if acctID == 0 {
		writeJSON(w, http.StatusUnauthorized, map[string]string{"error": "not authenticated"})
		return
	}
	name, ok := avatarName(r)
	if !ok {
		writeJSON(w, http.StatusBadRequest, map[string]string{"error": "bad character name"})
		return
	}
	ctx, cancel := context.WithTimeout(r.Context(), 5*time.Second)
	defer cancel()
	ship, err := s.store.avatarShip(ctx, acctID, name)
	if err != nil {
		avatarReadError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, ship)
}

// GET /api/avatar/{name}/skills -- learned skills with per-class max levels.
func (s *apiServer) handleAvatarSkills(w http.ResponseWriter, r *http.Request) {
	_, acctID := s.loggedIn(r)
	if acctID == 0 {
		writeJSON(w, http.StatusUnauthorized, map[string]string{"error": "not authenticated"})
		return
	}
	name, ok := avatarName(r)
	if !ok {
		writeJSON(w, http.StatusBadRequest, map[string]string{"error": "bad character name"})
		return
	}
	ctx, cancel := context.WithTimeout(r.Context(), 5*time.Second)
	defer cancel()
	skills, err := s.store.avatarSkills(ctx, acctID, name)
	if err != nil {
		avatarReadError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, skills)
}

// GET /api/galaxy -- the static galaxy topology (systems, sectors, gate edges).
// Auth-gated like the rest of the signed-in shell; the payload is cached hard
// server-side so this is a near-free map read.
func (s *apiServer) handleGalaxyMap(w http.ResponseWriter, r *http.Request) {
	_, acctID := s.loggedIn(r)
	if acctID == 0 {
		writeJSON(w, http.StatusUnauthorized, map[string]string{"error": "not authenticated"})
		return
	}
	ctx, cancel := context.WithTimeout(r.Context(), 8*time.Second)
	defer cancel()
	m, err := s.galaxy.getMap(ctx, time.Now())
	if err != nil {
		log.Printf("api: galaxy map: %v", err)
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": "internal error"})
		return
	}
	writeJSON(w, http.StatusOK, m)
}

// GET /api/galaxy/occupancy -- live online-player count per sector. Short-cached;
// the SPA polls it to drive the per-sector glow.
func (s *apiServer) handleGalaxyOccupancy(w http.ResponseWriter, r *http.Request) {
	_, acctID := s.loggedIn(r)
	if acctID == 0 {
		writeJSON(w, http.StatusUnauthorized, map[string]string{"error": "not authenticated"})
		return
	}
	ctx, cancel := context.WithTimeout(r.Context(), 5*time.Second)
	defer cancel()
	occ, err := s.galaxy.getOccupancy(ctx, time.Now())
	if err != nil {
		log.Printf("api: galaxy occupancy: %v", err)
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": "internal error"})
		return
	}
	writeJSON(w, http.StatusOK, occ)
}

// GET /api/galaxy/gateflow -- live directed sector-to-sector traffic over the
// trailing window, grouped by ordered (from,to) pair with an event count.
// Cached 60s server-side; the SPA polls it to drive the travelling-light
// animation along each lane.
func (s *apiServer) handleGalaxyGateFlow(w http.ResponseWriter, r *http.Request) {
	_, acctID := s.loggedIn(r)
	if acctID == 0 {
		writeJSON(w, http.StatusUnauthorized, map[string]string{"error": "not authenticated"})
		return
	}
	ctx, cancel := context.WithTimeout(r.Context(), 5*time.Second)
	defer cancel()
	flow, err := s.galaxy.getGateFlow(ctx, time.Now())
	if err != nil {
		log.Printf("api: galaxy gateflow: %v", err)
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": "internal error"})
		return
	}
	writeJSON(w, http.StatusOK, flow)
}

// GET /api/galaxy/me -- the logged-in account's own characters and the sector
// each is in, for personal star markers. Per-account, so auth-gated. Returns
// characters whether online or not.
func (s *apiServer) handleGalaxyMe(w http.ResponseWriter, r *http.Request) {
	_, acctID := s.loggedIn(r)
	if acctID == 0 {
		writeJSON(w, http.StatusUnauthorized, map[string]string{"error": "not authenticated"})
		return
	}
	ctx, cancel := context.WithTimeout(r.Context(), 5*time.Second)
	defer cancel()
	sb, err := s.galaxy.starbaseMap(ctx, time.Now())
	if err != nil {
		log.Printf("api: galaxy me: starbase: %v", err)
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": "internal error"})
		return
	}
	locs, err := s.store.myAvatarLocations(ctx, acctID, sb)
	if err != nil {
		log.Printf("api: galaxy me: locations(%d): %v", acctID, err)
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": "internal error"})
		return
	}
	writeJSON(w, http.StatusOK, map[string][]AvatarLocation{"avatars": locs})
}

// avatarReadError maps a Profile read error to a status. A name that is not one
// of the account's live characters is a 404 (ownership is enforced in the
// store), not a hint that the avatar exists elsewhere; anything else is a 500.
func avatarReadError(w http.ResponseWriter, err error) {
	if errors.Is(err, errCharacterNotFound) {
		writeJSON(w, http.StatusNotFound, map[string]string{"error": "character not found"})
		return
	}
	log.Printf("api: avatar read: %v", err)
	writeJSON(w, http.StatusInternalServerError, map[string]string{"error": "internal error"})
}
