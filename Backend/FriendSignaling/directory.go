package main

import (
	"net/http"
	"sort"
	"strings"
	"time"
	"unicode"
)

const applicationLease = 2 * time.Minute

type roomListing struct {
	ID       string `json:"id,omitempty"`
	Name     string `json:"name"`
	Clash    bool   `json:"clash"`
	Members  int    `json:"members"`
	Capacity int    `json:"capacity"`
}

type joinApplication struct {
	ID   string `json:"id"`
	Name string `json:"name"`
}

func (b *broker) findRoom(req request) *room {
	if req.Code != "" {
		return b.rooms[req.Code]
	}
	if req.RoomID != "" {
		for _, r := range b.rooms {
			if r.directoryID == req.RoomID {
				return r
			}
		}
	}
	return nil
}

func (r *room) acceptsApplications() bool {
	return r.open && r.listing != nil && r.listing.Members < r.listing.Capacity
}

func (b *broker) listRooms(now time.Time) []roomListing {
	var result []roomListing
	for _, r := range b.rooms {
		if r.expires.After(now) && r.acceptsApplications() {
			result = append(result, *r.listing)
		}
	}
	sort.Slice(result, func(i, j int) bool {
		if result[i].Name == result[j].Name {
			return result[i].ID < result[j].ID
		}
		return result[i].Name < result[j].Name
	})
	return result
}

func cleanName(value string) string {
	var name []rune
	for _, char := range strings.TrimSpace(value) {
		if unicode.IsPrint(char) && char != '<' && char != '>' {
			name = append(name, char)
			if len(name) == 20 {
				break
			}
		}
	}
	return strings.TrimSpace(string(name))
}

func (r *room) publish(listing *roomListing) (response, int) {
	if listing == nil {
		r.listing = nil
		for _, a := range r.applications {
			a.declined = true
		}
		return response{}, http.StatusOK
	}
	name := cleanName(listing.Name)
	limit := 32
	if listing.Clash {
		limit = 12
	}
	if name == "" || listing.Capacity < 2 || listing.Capacity > limit ||
		listing.Members < 1 || listing.Members > listing.Capacity {
		return response{Error: "invalid_request"}, http.StatusBadRequest
	}
	r.listing = &roomListing{ID: r.directoryID, Name: name, Clash: listing.Clash,
		Members: listing.Members, Capacity: listing.Capacity}
	return response{}, http.StatusOK
}

func (r *room) apply(name, source string, now time.Time) (response, int) {
	if !r.acceptsApplications() || len(r.applications) >= queueLimit {
		return response{Error: "room_busy"}, http.StatusConflict
	}
	name = cleanName(name)
	if name == "" {
		return response{Error: "invalid_request"}, http.StatusBadRequest
	}
	pending := 0
	for _, a := range r.applications {
		if a.source == source {
			pending++
		}
	}
	if pending >= 2 {
		return response{Error: "room_busy"}, http.StatusConflict
	}
	a := &attempt{id: key(12), token: key(24), applicant: name, source: source, expires: now.Add(applicationLease)}
	r.applications = append(r.applications, a)
	return response{Token: a.token}, http.StatusOK
}

func (r *room) pendingApplications() []joinApplication {
	var result []joinApplication
	for _, a := range r.applications {
		if !a.declined {
			result = append(result, joinApplication{ID: a.id, Name: a.applicant})
		}
	}
	return result
}

func (r *room) review(id, decision string, now time.Time) (response, int) {
	if decision != "approve" && decision != "decline" {
		return response{Error: "invalid_request"}, http.StatusBadRequest
	}
	for i, a := range r.applications {
		if a.id != id {
			continue
		}
		if decision == "decline" {
			a.declined = true
			return response{}, http.StatusOK
		}
		if a.declined {
			break
		}
		if !r.acceptsApplications() || len(r.waiting) >= queueLimit ||
			r.listing.Members+len(r.waiting) >= r.listing.Capacity {
			return response{Error: "room_busy"}, http.StatusConflict
		}
		r.applications = removeAttempt(r.applications, i)
		a.expires = now.Add(joinLease)
		r.waiting = append(r.waiting, a)
		return response{}, http.StatusOK
	}
	for _, a := range r.waiting {
		if a.id == id && a.applicant != "" && decision == "approve" {
			return response{}, http.StatusOK
		}
	}
	return response{Error: "attempt_closed"}, http.StatusNotFound
}

func (r *room) applicationStatus(req request, token string) (response, int) {
	for i, a := range r.applications {
		if !sameToken(a.token, token) {
			continue
		}
		if req.Cancel {
			r.applications = removeAttempt(r.applications, i)
			return response{}, http.StatusOK
		}
		if req.Reply != "" {
			return response{Error: "invalid_request"}, http.StatusConflict
		}
		if a.declined {
			return response{Status: "declined"}, http.StatusOK
		}
		return response{Status: "pending"}, http.StatusOK
	}
	return response{Error: "attempt_closed"}, http.StatusNotFound
}
