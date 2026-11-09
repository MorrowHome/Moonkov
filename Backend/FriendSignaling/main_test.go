package main

import (
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"
)

func TestExchangeIsPrivateAndFinishIsScoped(t *testing.T) {
	b := newBroker()
	now := time.Now()
	call := func(operation, token string, req request, want int) response {
		t.Helper()
		result, status := b.handle(operation, "198.51.100.1", token, req, now)
		if status != want {
			t.Fatalf("%s returned %d, want %d (%s)", operation, status, want, result.Error)
		}
		return result
	}

	owner := call("create", "", request{}, http.StatusOK)
	room := request{Code: owner.Code}
	guest := call("join", "", room, http.StatusOK)
	other := call("join", "", room, http.StatusOK)
	open := request{Code: owner.Code, Open: true}
	call("host", guest.Token, open, http.StatusNotFound)
	call("close", guest.Token, room, http.StatusNotFound)
	call("guest", owner.Token, room, http.StatusNotFound)
	first := call("host", owner.Token, open, http.StatusOK)
	offer := request{Code: owner.Code, Open: true, Attempt: first.Attempt, Offer: "first offer"}
	call("host", owner.Token, offer, http.StatusOK)
	call("host", owner.Token, offer, http.StatusOK)
	if call("guest", guest.Token, room, http.StatusOK).Offer != offer.Offer ||
		call("guest", other.Token, room, http.StatusOK).Offer != "" {
		t.Fatal("the invitation must only be visible to the selected guest")
	}
	answer := request{Code: owner.Code, Reply: "first reply"}
	call("guest", other.Token, answer, http.StatusConflict)
	call("guest", guest.Token, answer, http.StatusOK)
	call("guest", guest.Token, answer, http.StatusOK)
	if call("host", owner.Token, offer, http.StatusOK).Reply != answer.Reply {
		t.Fatal("the host did not receive the guest's reply")
	}
	finish := request{Code: owner.Code, Open: true, Attempt: first.Attempt, Finish: true}
	call("host", owner.Token, finish, http.StatusOK)
	call("host", owner.Token, finish, http.StatusOK)
	next := call("host", owner.Token, open, http.StatusOK)
	if next.Attempt == "" || next.Attempt == first.Attempt {
		t.Fatal("repeated finish removed the next guest")
	}
	call("guest", guest.Token, room, http.StatusNotFound)
	call("host", owner.Token, room, http.StatusOK)
	call("join", "", room, http.StatusConflict)
	call("close", owner.Token, room, http.StatusOK)
	call("join", "", room, http.StatusNotFound)
}

func TestLeasesAndQueueBounds(t *testing.T) {
	b := newBroker()
	now := time.Now()
	owner, _ := b.handle("create", "host", "", request{}, now)
	room := request{Code: owner.Code}
	for i := 0; i < queueLimit; i++ {
		if _, status := b.handle("join", "guest", "", room, now); status != http.StatusOK {
			t.Fatalf("join %d returned %d", i, status)
		}
	}
	if _, status := b.handle("join", "guest", "", room, now); status != http.StatusConflict {
		t.Fatalf("full queue returned %d", status)
	}
	b.prune(now.Add(joinLease))
	if len(b.rooms[owner.Code].waiting) != 0 {
		t.Fatal("expired attempts stayed in memory")
	}
	if _, status := b.handle("host", "host", owner.Token, room, now.Add(roomLease)); status != http.StatusNotFound {
		t.Fatalf("expired room could be renewed: %d", status)
	}
}

func TestPublicRequestLimits(t *testing.T) {
	b := newBroker()
	bad := []string{strings.Repeat(" ", 16_400) + "{}", "{}{}", `{"offer":"` + strings.Repeat("A", payloadLimit+1) + `"}`}
	for _, body := range bad {
		w := httptest.NewRecorder()
		b.ServeHTTP(w, httptest.NewRequest(http.MethodPost, prefix+"create", strings.NewReader(body)))
		if w.Code != http.StatusBadRequest {
			t.Fatalf("invalid request returned %d", w.Code)
		}
	}
	now := time.Now()
	for i := 0; i < 49; i++ {
		_, status := b.handle("join", "one-client", "", request{Code: "AAAAAAAA"}, now)
		if i < 48 && status != http.StatusNotFound || i == 48 && status != http.StatusTooManyRequests {
			t.Fatalf("guess %d returned %d", i, status)
		}
	}
}
