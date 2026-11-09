package main

import (
	"crypto/rand"
	"crypto/subtle"
	"encoding/base64"
	"encoding/json"
	"flag"
	"io"
	"log"
	"net"
	"net/http"
	"strings"
	"sync"
	"time"
)

const (
	prefix       = "/friend-signaling/v1/"
	alphabet     = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ"
	roomLease    = 2 * time.Minute
	joinLease    = time.Minute
	roomLimit    = 128
	queueLimit   = 6
	payloadLimit = 14_000
)

type request struct {
	Code     string       `json:"code"`
	RoomID   string       `json:"roomId"`
	Attempt  string       `json:"attempt"`
	Offer    string       `json:"offer"`
	Reply    string       `json:"reply"`
	Name     string       `json:"name"`
	Decision string       `json:"decision"`
	Listing  *roomListing `json:"listing"`
	Open     bool         `json:"open"`
	Finish   bool         `json:"finish"`
	Cancel   bool         `json:"cancel"`
}

type response struct {
	Code         string            `json:"code,omitempty"`
	Token        string            `json:"token,omitempty"`
	Attempt      string            `json:"attempt,omitempty"`
	Offer        string            `json:"offer,omitempty"`
	Reply        string            `json:"reply,omitempty"`
	Error        string            `json:"error,omitempty"`
	Status       string            `json:"status,omitempty"`
	Rooms        []roomListing     `json:"rooms,omitempty"`
	Applications []joinApplication `json:"applications,omitempty"`
}

type attempt struct {
	id, token, offer, reply string
	applicant, source       string
	declined                bool
	expires                 time.Time
}

type room struct {
	owner, source, directoryID string
	open                       bool
	expires                    time.Time
	waiting, applications      []*attempt
	listing                    *roomListing
}

type usage struct{ created, joined, browsed int }

type broker struct {
	mu          sync.Mutex
	rooms       map[string]*room
	sources     map[string]usage
	usageMinute time.Time
	workers     chan struct{}
}

func newBroker() *broker {
	return &broker{rooms: make(map[string]*room), workers: make(chan struct{}, 32)}
}

func key(size int) string {
	bytes := make([]byte, size)
	if _, err := rand.Read(bytes); err != nil {
		panic("random source unavailable")
	}
	return base64.RawURLEncoding.EncodeToString(bytes)
}

func roomCode() string {
	var bytes [8]byte
	if _, err := rand.Read(bytes[:]); err != nil {
		panic("random source unavailable")
	}
	for i, value := range bytes {
		bytes[i] = alphabet[value&31]
	}
	return string(bytes[:])
}

func (b *broker) ServeHTTP(w http.ResponseWriter, r *http.Request) {
	w.Header().Set("Cache-Control", "no-store")
	w.Header().Set("Content-Type", "application/json")
	if r.Method == http.MethodGet && r.URL.Path == prefix+"health" {
		_, _ = io.WriteString(w, `{"service":"moon-friend-signaling","version":1}`)
		return
	}
	operation := strings.TrimPrefix(r.URL.Path, prefix)
	if r.Method != http.MethodPost || operation == r.URL.Path {
		write(w, http.StatusNotFound, response{Error: "not_found"})
		return
	}
	select {
	case b.workers <- struct{}{}:
		defer func() { <-b.workers }()
	default:
		write(w, http.StatusServiceUnavailable, response{Error: "busy"})
		return
	}
	var req request
	r.Body = http.MaxBytesReader(w, r.Body, 16_384)
	decoder := json.NewDecoder(r.Body)
	decoder.DisallowUnknownFields()
	if decoder.Decode(&req) != nil || decoder.Decode(new(any)) != io.EOF ||
		len(req.Offer) > payloadLimit || len(req.Reply) > payloadLimit {
		write(w, http.StatusBadRequest, response{Error: "invalid_request"})
		return
	}
	token, _ := strings.CutPrefix(r.Header.Get("Authorization"), "Bearer ")
	// The loopback proxy overwrites this header with the actual TCP peer.
	ip := net.ParseIP(r.Header.Get("X-Moon-Friend-IP"))
	source, _, _ := net.SplitHostPort(r.RemoteAddr)
	if ip != nil {
		source = ip.String()
	}
	reply, status := b.handle(operation, source, token, req, time.Now())
	write(w, status, reply)
}

func write(w http.ResponseWriter, status int, value response) {
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(value)
}

func (b *broker) allow(source, operation string, now time.Time) bool {
	minute := now.Truncate(time.Minute)
	if minute != b.usageMinute {
		b.usageMinute, b.sources = minute, make(map[string]usage)
	}
	count, exists := b.sources[source]
	if !exists && len(b.sources) >= 4096 {
		return false
	}
	switch operation {
	case "create":
		if count.created >= 8 {
			return false
		}
		count.created++
	case "list":
		if count.browsed >= 60 {
			return false
		}
		count.browsed++
	default:
		if count.joined >= 48 {
			return false
		}
		count.joined++
	}
	b.sources[source] = count
	return true
}

func (b *broker) handle(operation, source, token string, req request, now time.Time) (response, int) {
	b.mu.Lock()
	defer b.mu.Unlock()
	if operation == "create" {
		b.prune(now)
		owned := 0
		for _, r := range b.rooms {
			if r.source == source {
				owned++
			}
		}
		if !b.allow(source, operation, now) || owned >= 4 || len(b.rooms) >= roomLimit {
			return response{Error: "busy"}, http.StatusTooManyRequests
		}
		code := roomCode()
		for b.rooms[code] != nil {
			code = roomCode()
		}
		token := key(24)
		b.rooms[code] = &room{owner: token, source: source, directoryID: key(12), open: true, expires: now.Add(roomLease)}
		return response{Code: code, Token: token}, http.StatusOK
	}

	if (operation == "join" || operation == "apply" || operation == "list") && !b.allow(source, operation, now) {
		return response{Error: "busy"}, http.StatusTooManyRequests
	}
	if operation == "list" {
		return response{Rooms: b.listRooms(now)}, http.StatusOK
	}
	r := b.findRoom(req)
	if r == nil || !r.expires.After(now) {
		return response{Error: "room_closed"}, http.StatusNotFound
	}
	r.expireAttempts(now)
	switch operation {
	case "join":
		if req.Code == "" || !r.open || len(r.waiting) >= queueLimit {
			return response{Error: "room_busy"}, http.StatusConflict
		}
		a := &attempt{id: key(12), token: key(24), expires: now.Add(joinLease)}
		r.waiting = append(r.waiting, a)
		return response{Token: a.token}, http.StatusOK
	case "apply":
		if req.RoomID != r.directoryID || req.Code != "" {
			return response{Error: "room_closed"}, http.StatusNotFound
		}
		return r.apply(req.Name, source, now)
	case "host", "close", "publish", "review":
		if req.Code == "" || !sameToken(r.owner, token) {
			return response{Error: "room_closed"}, http.StatusNotFound
		}
		switch operation {
		case "close":
			delete(b.rooms, req.Code)
			return response{}, http.StatusOK
		case "publish":
			return r.publish(req.Listing)
		case "review":
			return r.review(req.Attempt, req.Decision, now)
		}
		r.expires, r.open = now.Add(roomLease), req.Open
		if r.listing != nil && req.Listing != nil {
			// The heartbeat updates counts for an already published room.
			// An in-flight heartbeat cannot publish a room again after a hide.
			r.listing.Members = min(max(req.Listing.Members, 1), r.listing.Capacity)
		}
		return r.host(req)
	case "guest":
		return r.guest(req, token)
	default:
		return response{Error: "not_found"}, http.StatusNotFound
	}
}

// Called under the broker lock. Idle rooms also lose their SDP on the timer.
func (b *broker) prune(now time.Time) {
	for code, r := range b.rooms {
		if !r.expires.After(now) {
			delete(b.rooms, code)
		} else {
			r.expireAttempts(now)
		}
	}
}

func sameToken(expected, supplied string) bool {
	return subtle.ConstantTimeCompare([]byte(expected), []byte(supplied)) == 1
}

func liveAttempts(queue []*attempt, now time.Time) []*attempt {
	kept := queue[:0]
	for _, a := range queue {
		if a.expires.After(now) {
			kept = append(kept, a)
		}
	}
	clear(queue[len(kept):])
	return kept
}

func (r *room) expireAttempts(now time.Time) {
	r.waiting = liveAttempts(r.waiting, now)
	r.applications = liveAttempts(r.applications, now)
}

func removeAttempt(queue []*attempt, i int) []*attempt {
	last := len(queue) - 1
	copy(queue[i:], queue[i+1:])
	queue[last] = nil
	return queue[:last]
}

func (r *room) host(req request) (response, int) {
	if req.Attempt == "" && !req.Finish {
		result := response{Applications: r.pendingApplications()}
		if r.open && len(r.waiting) > 0 {
			result.Attempt = r.waiting[0].id
		}
		return result, http.StatusOK
	}
	if len(r.waiting) == 0 {
		if req.Attempt != "" && !req.Finish {
			return response{Error: "attempt_closed"}, http.StatusNotFound
		}
		return response{}, http.StatusOK
	}
	a := r.waiting[0]
	if req.Finish {
		if req.Attempt == a.id {
			r.waiting = removeAttempt(r.waiting, 0)
		}
		return response{}, http.StatusOK
	}
	if req.Attempt != a.id {
		return response{Error: "attempt_closed"}, http.StatusNotFound
	}
	if req.Offer != "" {
		if a.offer != "" && a.offer != req.Offer {
			return response{Error: "invalid_request"}, http.StatusConflict
		}
		a.offer = req.Offer
	}
	return response{Reply: a.reply}, http.StatusOK
}

func (r *room) guest(req request, token string) (response, int) {
	for i, a := range r.waiting {
		if !sameToken(a.token, token) {
			continue
		}
		if req.Cancel {
			r.waiting = removeAttempt(r.waiting, i)
			return response{}, http.StatusOK
		}
		if req.Reply != "" {
			if a.offer == "" || a.reply != "" && a.reply != req.Reply {
				return response{Error: "invalid_request"}, http.StatusConflict
			}
			a.reply = req.Reply
			return response{}, http.StatusOK
		}
		result := response{Offer: a.offer}
		if a.applicant != "" {
			result.Status = "approved"
		}
		return result, http.StatusOK
	}
	return r.applicationStatus(req, token)
}

func main() {
	address := flag.String("listen", "127.0.0.1:5081", "loopback HTTP listener")
	flag.Parse()
	host, _, err := net.SplitHostPort(*address)
	if err != nil || host != "127.0.0.1" {
		log.Fatal("listen must use 127.0.0.1")
	}
	b := newBroker()
	go func() {
		ticker := time.NewTicker(10 * time.Second)
		defer ticker.Stop()
		for now := range ticker.C {
			b.mu.Lock()
			b.prune(now)
			b.mu.Unlock()
		}
	}()
	server := &http.Server{Addr: *address, Handler: b,
		ReadHeaderTimeout: 3 * time.Second, ReadTimeout: 6 * time.Second,
		WriteTimeout: 6 * time.Second, IdleTimeout: 30 * time.Second, MaxHeaderBytes: 8192}
	log.Printf("friend signaling listening on %s", *address)
	log.Fatal(server.ListenAndServe())
}
