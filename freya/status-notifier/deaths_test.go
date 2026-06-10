package main

import (
	"testing"
	"time"
)

func TestPlayerNameFromContent(t *testing.T) {
	cases := []struct{ in, want string }{
		{"Player Veretjd (C25) was destroyed by Red Dragon Syndicate in Kinshasa-Mbali.", "Veretjd"},
		{"Player Veretjd was jumpstarted in Kinshasa-Mbali.", "Veretjd"},
		{"Player Grieverje (C0) was destroyed by an unknown enemy in an unknown sector.", "Grieverje"},
		{"Server started.", ""},
		{"Player", ""},
		{"", ""},
	}
	for _, c := range cases {
		if got := playerNameFromContent(c.in); got != c.want {
			t.Errorf("playerNameFromContent(%q) = %q, want %q", c.in, got, c.want)
		}
	}
}

func TestStrikethrough(t *testing.T) {
	if got := strikethrough("x"); got != "~~x~~" {
		t.Errorf("strikethrough = %q", got)
	}
}

// fakeDeliverer records sends and edits so deliverEvent can be exercised without
// a live Discord gateway.
type fakeDeliverer struct {
	nextID   int
	sendFail bool
	editFail bool
	sent     []string          // contents posted, in order
	edits    map[string]string // messageID -> last edited content
}

func newFakeDeliverer() *fakeDeliverer {
	return &fakeDeliverer{edits: map[string]string{}}
}

func (f *fakeDeliverer) send(content string) (string, bool) {
	if f.sendFail {
		return "", false
	}
	f.nextID++
	id := "msg" + string(rune('0'+f.nextID))
	f.sent = append(f.sent, content)
	return id, true
}

func (f *fakeDeliverer) edit(messageID, content string) bool {
	if f.editFail {
		return false
	}
	f.edits[messageID] = content
	return true
}

// A wreck then a jumpstart within the window: the wreck is posted, and the
// jumpstart EDITS that message (strike + append) rather than posting anew.
func TestDeliverEvent_WreckThenJumpstart_Edits(t *testing.T) {
	d := newFakeDeliverer()
	tr := newDeathTracker()
	t0 := time.Unix(1_000_000, 0)

	wreck := event{id: 1, kind: "player_destroyed",
		content: "Player Veretjd (C25) was destroyed by Red Dragon Syndicate in Kinshasa-Mbali."}
	if !deliverEvent(d, tr, wreck, t0) {
		t.Fatal("wreck should be delivered")
	}
	if len(d.sent) != 1 || d.sent[0] != wreck.content {
		t.Fatalf("wreck not posted as-is: %v", d.sent)
	}

	jump := event{id: 2, kind: "jumpstarted",
		content: "Player Veretjd was jumpstarted in Kinshasa-Mbali."}
	if !deliverEvent(d, tr, jump, t0.Add(5*time.Minute)) {
		t.Fatal("jumpstart should be consumed")
	}
	// No second post; the original message was edited instead.
	if len(d.sent) != 1 {
		t.Fatalf("jumpstart should not post a new message, sent=%v", d.sent)
	}
	want := "~~" + wreck.content + "~~ " + jump.content
	if got := d.edits["msg1"]; got != want {
		t.Fatalf("edit = %q, want %q", got, want)
	}
}

// A jumpstart arriving after the 30-minute window leaves the wreck line alone and
// posts nothing.
func TestDeliverEvent_JumpstartOutsideWindow_Dropped(t *testing.T) {
	d := newFakeDeliverer()
	tr := newDeathTracker()
	t0 := time.Unix(1_000_000, 0)

	deliverEvent(d, tr, event{kind: "player_destroyed",
		content: "Player Veretjd (C25) was destroyed by Mob in Sector."}, t0)

	jump := event{kind: "jumpstarted", content: "Player Veretjd was jumpstarted in Sector."}
	if !deliverEvent(d, tr, jump, t0.Add(31*time.Minute)) {
		t.Fatal("stale jumpstart should still be consumed")
	}
	if len(d.edits) != 0 {
		t.Fatalf("stale jumpstart must not edit: %v", d.edits)
	}
	if len(d.sent) != 1 {
		t.Fatalf("stale jumpstart must not post: %v", d.sent)
	}
}

// A jumpstart with no matching wreck (towed to station / sidecar restarted) posts
// nothing and is consumed.
func TestDeliverEvent_JumpstartNoWreck_Dropped(t *testing.T) {
	d := newFakeDeliverer()
	tr := newDeathTracker()
	t0 := time.Unix(1_000_000, 0)

	jump := event{kind: "jumpstarted", content: "Player Lonewolf was jumpstarted in Sector."}
	if !deliverEvent(d, tr, jump, t0) {
		t.Fatal("orphan jumpstart should be consumed")
	}
	if len(d.sent) != 0 || len(d.edits) != 0 {
		t.Fatalf("orphan jumpstart must do nothing: sent=%v edits=%v", d.sent, d.edits)
	}
}

// A failed wreck post leaves the row unsent (retry) and records no tracker entry.
func TestDeliverEvent_WreckSendFails_Retry(t *testing.T) {
	d := newFakeDeliverer()
	d.sendFail = true
	tr := newDeathTracker()
	ok := deliverEvent(d, tr, event{kind: "player_destroyed",
		content: "Player Veretjd (C25) was destroyed by Mob in Sector."}, time.Unix(1, 0))
	if ok {
		t.Fatal("failed send should return false (leave unsent)")
	}
}

// A failed edit leaves the row unsent AND keeps the wreck record so the retry can
// still find it.
func TestDeliverEvent_EditFails_KeepsRecordForRetry(t *testing.T) {
	d := newFakeDeliverer()
	tr := newDeathTracker()
	t0 := time.Unix(1_000_000, 0)
	deliverEvent(d, tr, event{kind: "player_destroyed",
		content: "Player Veretjd (C25) was destroyed by Mob in Sector."}, t0)

	d.editFail = true
	jump := event{kind: "jumpstarted", content: "Player Veretjd was jumpstarted in Sector."}
	if deliverEvent(d, tr, jump, t0.Add(time.Minute)) {
		t.Fatal("failed edit should return false")
	}
	// Record kept: a subsequent successful edit must work.
	d.editFail = false
	if !deliverEvent(d, tr, jump, t0.Add(2*time.Minute)) {
		t.Fatal("retry edit should succeed and consume")
	}
	if _, ok := d.edits["msg1"]; !ok {
		t.Fatal("retry should have edited msg1")
	}
}

// An ordinary kind is a plain post; failure leaves it unsent.
func TestDeliverEvent_OrdinaryKind(t *testing.T) {
	d := newFakeDeliverer()
	tr := newDeathTracker()
	if !deliverEvent(d, tr, event{kind: "login", content: "X logged in"}, time.Unix(1, 0)) {
		t.Fatal("login should post")
	}
	d.sendFail = true
	if deliverEvent(d, tr, event{kind: "login", content: "Y logged in"}, time.Unix(1, 0)) {
		t.Fatal("failed login send should return false")
	}
}
