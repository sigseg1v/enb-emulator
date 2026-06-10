// SPDX-License-Identifier: MIT
// Freya Online -- background sweepers.
//
// Two periodic jobs keep the AH/mailbox state correct without depending on a
// user request to trigger settlement:
//   - expire auctions (resolve sales / return unsold items + refund deposits);
//   - delete mail past its 90-day expiry.
// Both run on a single ticker and stop when the context is cancelled.

package main

import (
	"context"
	"log"
	"time"
)

func startSweepers(ctx context.Context, store *Store) {
	go func() {
		t := time.NewTicker(sweepInterval)
		defer t.Stop()
		// Run once at startup so a restart settles anything already overdue.
		runSweep(ctx, store)
		for {
			select {
			case <-ctx.Done():
				return
			case <-t.C:
				runSweep(ctx, store)
			}
		}
	}()
}

func runSweep(ctx context.Context, store *Store) {
	sweepCtx, cancel := context.WithTimeout(ctx, 60*time.Second)
	defer cancel()

	if n, err := store.sweepExpiredAuctions(sweepCtx); err != nil {
		log.Printf("sweep: auctions: %v", err)
	} else if n > 0 {
		log.Printf("sweep: resolved %d expired auction(s)", n)
	}

	if n, err := store.sweepExpiredMail(sweepCtx); err != nil {
		log.Printf("sweep: mail: %v", err)
	} else if n > 0 {
		log.Printf("sweep: deleted %d expired mail message(s)", n)
	}
}
