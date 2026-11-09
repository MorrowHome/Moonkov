package main

import (
	"net/http"
	"testing"
	"time"
)

func TestDirectoryRequiresApprovalAndKeepsRoomCodePrivate(t *testing.T) {
	b := newBroker()
	now := time.Now()
	call := func(op, token string, req request, want int) response {
		t.Helper()
		result, status := b.handle(op, "192.0.2.1", token, req, now)
		if status != want {
			t.Fatalf("%s returned %d, want %d (%s)", op, status, want, result.Error)
		}
		return result
	}
	owner := call("create", "", request{}, http.StatusOK)
	directory := request{RoomID: b.rooms[owner.Code].directoryID}
	lease := request{Code: owner.Code, Open: true}
	list := func() []roomListing { return call("list", "", request{}, http.StatusOK).Rooms }
	if len(list()) != 0 {
		t.Fatal("new rooms must be private")
	}
	call("apply", "", request{RoomID: directory.RoomID, Name: "Guest"}, http.StatusConflict)
	publish := request{Code: owner.Code, Listing: &roomListing{Name: " Moon <Squad> ", Members: 1, Capacity: 4}}
	call("publish", "", publish, http.StatusNotFound)
	call("publish", owner.Token, publish, http.StatusOK)
	if rooms := list(); len(rooms) != 1 || rooms[0].ID != directory.RoomID || rooms[0].ID == owner.Code ||
		rooms[0].Name != "Moon Squad" || rooms[0].Members != 1 || rooms[0].Capacity != 4 {
		t.Fatal("incorrect public listing")
	}
	call("join", "", directory, http.StatusConflict)
	call("apply", "", request{Code: owner.Code, RoomID: directory.RoomID, Name: "Guest"}, http.StatusNotFound)
	guest := call("apply", "", request{RoomID: directory.RoomID, Name: "Guest"}, http.StatusOK)
	poll := call("host", owner.Token, lease, http.StatusOK)
	if poll.Attempt != "" || len(poll.Applications) != 1 || poll.Applications[0].Name != "Guest" {
		t.Fatal("the host must review an application before exchanging signals")
	}
	if pending := call("guest", guest.Token, directory, http.StatusOK); pending.Status != "pending" || pending.Offer != "" {
		t.Fatal("pending guests must wait for the host")
	}
	call("guest", guest.Token, request{RoomID: directory.RoomID, Reply: "early reply"}, http.StatusConflict)
	call("guest", owner.Token, directory, http.StatusNotFound)
	codeGuest := call("join", "", lease, http.StatusOK)
	codeAttempt := call("host", owner.Token, lease, http.StatusOK).Attempt
	if codeAttempt == "" || codeAttempt == poll.Applications[0].ID {
		t.Fatal("a room code should still work while an application is pending")
	}
	review := request{Code: owner.Code, Attempt: poll.Applications[0].ID, Decision: "approve"}
	call("review", guest.Token, review, http.StatusNotFound)
	call("review", owner.Token, review, http.StatusOK)
	call("review", owner.Token, review, http.StatusOK)
	finish := request{Code: owner.Code, Open: true, Attempt: codeAttempt, Finish: true}
	call("host", owner.Token, finish, http.StatusOK)
	call("host", owner.Token, finish, http.StatusOK)
	approved := call("host", owner.Token, lease, http.StatusOK)
	if approved.Attempt != review.Attempt || len(approved.Applications) != 0 {
		t.Fatal("the approved application must become the next exchange")
	}
	call("guest", codeGuest.Token, directory, http.StatusNotFound)
	call("host", owner.Token, request{Code: owner.Code, Open: true, Attempt: approved.Attempt, Offer: "public guest offer"}, http.StatusOK)
	if poll := call("guest", guest.Token, directory, http.StatusOK); poll.Status != "approved" || poll.Offer != "public guest offer" {
		t.Fatal("the approved guest did not receive their offer")
	}
	call("guest", guest.Token, request{RoomID: directory.RoomID, Reply: "public guest reply"}, http.StatusOK)
	if poll := call("host", owner.Token, request{Code: owner.Code, Open: true, Attempt: approved.Attempt}, http.StatusOK); poll.Reply != "public guest reply" {
		t.Fatal("the host did not receive the approved guest's reply")
	}
	call("close", owner.Token, lease, http.StatusOK)
	if len(list()) != 0 {
		t.Fatal("the closed room remained listed")
	}
}

func TestDirectoryBoundsVisibilityAndApplicationLeases(t *testing.T) {
	b := newBroker()
	now := time.Now()
	call := func(op, source, token string, req request, want int) response {
		t.Helper()
		result, status := b.handle(op, source, token, req, now)
		if status != want {
			t.Fatalf("%s returned %d, want %d (%s)", op, status, want, result.Error)
		}
		return result
	}
	owner := call("create", "host", "", request{}, http.StatusOK)
	lease := request{Code: owner.Code, Open: true}
	publish := request{Code: owner.Code, Listing: &roomListing{Name: "Open Room", Members: 1, Capacity: 2}}
	call("publish", "host", owner.Token, publish, http.StatusOK)
	directory := request{RoomID: b.rooms[owner.Code].directoryID, Name: "Guest"}
	guest := call("apply", "guest", "", directory, http.StatusOK)
	second := call("apply", "guest", "", directory, http.StatusOK)
	call("apply", "guest", "", directory, http.StatusConflict)
	for _, source := range []string{"g2", "g3", "g4", "g5"} {
		call("apply", source, "", directory, http.StatusOK)
	}
	call("apply", "g6", "", directory, http.StatusConflict)
	applications := call("host", "host", owner.Token, lease, http.StatusOK).Applications
	decline := request{Code: owner.Code, Attempt: applications[0].ID, Decision: "decline"}
	call("review", "host", owner.Token, decline, http.StatusOK)
	if call("guest", "guest", guest.Token, directory, http.StatusOK).Status != "declined" {
		t.Fatal("the rejection was not delivered")
	}
	decline.Decision = "approve"
	call("review", "host", owner.Token, decline, http.StatusNotFound)
	cancel := request{RoomID: directory.RoomID, Cancel: true}
	call("guest", "guest", second.Token, cancel, http.StatusOK)
	call("guest", "guest", second.Token, directory, http.StatusNotFound)
	third := call("apply", "g6", "", directory, http.StatusOK)
	full := request{Code: owner.Code, Listing: &roomListing{Members: 2}}
	call("host", "host", owner.Token, full, http.StatusOK)
	approve := request{Code: owner.Code, Attempt: applications[2].ID, Decision: "approve"}
	call("review", "host", owner.Token, approve, http.StatusConflict)
	call("apply", "g7", "", directory, http.StatusConflict)
	list := func() []roomListing { return call("list", "guest", "", request{}, http.StatusOK).Rooms }
	if len(list()) != 0 {
		t.Fatal("a full room remained listed")
	}
	call("publish", "host", owner.Token, lease, http.StatusOK)
	call("host", "host", owner.Token, request{Code: owner.Code, Open: true, Listing: &roomListing{Members: 1}}, http.StatusOK)
	if len(list()) != 0 || call("guest", "g6", third.Token, directory, http.StatusOK).Status != "declined" {
		t.Fatal("a stale heartbeat reopened the room or a pending application survived hiding")
	}
	call("join", "guest", "", lease, http.StatusOK)
	now = now.Add(time.Minute)
	call("host", "host", owner.Token, lease, http.StatusOK)
	now = now.Add(time.Minute)
	call("guest", "g6", third.Token, directory, http.StatusNotFound)
	call("publish", "host", owner.Token, publish, http.StatusOK)
	if len(list()) != 1 {
		t.Fatal("the host could not republish the room")
	}
	now = now.Add(roomLease)
	if len(list()) != 0 {
		t.Fatal("the expired room remained listed")
	}
}
