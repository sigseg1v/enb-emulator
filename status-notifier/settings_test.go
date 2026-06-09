package main

import "testing"

func TestIsKnownKind(t *testing.T) {
	for _, k := range notificationKinds {
		if !isKnownKind(k) {
			t.Errorf("isKnownKind(%q) = false, want true (it is in the allowlist)", k)
		}
	}
	for _, bad := range []string{"", "Login", "logout ", "admin", "drop table", "levelu"} {
		if isKnownKind(bad) {
			t.Errorf("isKnownKind(%q) = true, want false", bad)
		}
	}
}

func TestKindChoices(t *testing.T) {
	choices := kindChoices()
	if len(choices) != len(notificationKinds) {
		t.Fatalf("kindChoices len = %d, want %d", len(choices), len(notificationKinds))
	}
	for i, c := range choices {
		if c.Name != notificationKinds[i] {
			t.Errorf("choice[%d].Name = %q, want %q", i, c.Name, notificationKinds[i])
		}
		if v, ok := c.Value.(string); !ok || v != notificationKinds[i] {
			t.Errorf("choice[%d].Value = %v, want %q", i, c.Value, notificationKinds[i])
		}
	}
}
