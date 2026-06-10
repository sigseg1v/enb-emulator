package main

import "testing"

// buildDSN assembles a libpq URL from the DB_* env and escapes the password.
func TestBuildDSNFromParts(t *testing.T) {
	t.Setenv("DATABASE_URL", "")
	t.Setenv("DB_HOST", "postgres:5432")
	t.Setenv("DB_USER", "net7")
	t.Setenv("DB_PASS", "p@ss/word")
	t.Setenv("DB_NAME", "net7_user")
	dsn := buildDSN()
	want := "postgres://net7:p%40ss%2Fword@postgres:5432/net7_user?sslmode=disable"
	if dsn != want {
		t.Fatalf("buildDSN = %q, want %q", dsn, want)
	}
}

// DATABASE_URL overrides the DB_* pieces entirely.
func TestBuildDSNOverride(t *testing.T) {
	t.Setenv("DATABASE_URL", "postgres://u:p@h:1/db")
	if got := buildDSN(); got != "postgres://u:p@h:1/db" {
		t.Fatalf("buildDSN override = %q", got)
	}
}
