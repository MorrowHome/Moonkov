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
	Code    string `json:"code"`
	Attempt string `json:"attempt"`
	Offer   string `json:"offer"`
	Reply   string `json:"reply"`
	Open    bool   `json:"open"`
	Finish  bool   `json:"finish"`
	Cancel  bool   `json:"cancel"`
}

type response struct {
	Code    string `json:"code,omitempty"`
	Token   string `json:"token,omitempty"`
	Attempt string `json:"attempt,omitempty"`
	Offer   string `json:"offer,omitempty"`
	Reply   string `json:"reply,omitempty"`
	Error   string `json:"error,omitempty"`
}

type attempt struct {
	id, token, offer, reply string
	expires                 time.Time
}

type room struct {
	owner, source string
	open          bool
	expires       time.Time
	waiting       []*attempt
}

type usage struct{ created, joined int }

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

func (b *broker) allow(source string, creating bool, now time.Time) bool {
	minute := now.Truncate(time.Minute)
	if minute != b.usageMinute {
		b.usageMinute, b.sources = minute, make(map[string]usage)
	}
	count, exists := b.sources[source]
	if !exists && len(b.sources) >= 4096 {
		return false
	}
	if creating {
		if count.created >= 8 {
			return false
		}
		count.created++
	} else {
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
		if !b.allow(source, true, now) || owned >= 4 || len(b.rooms) >= roomLimit {
			return response{Error: "busy"}, http.StatusTooManyRequests
		}
		code := roomCode()
		for b.rooms[code] != nil {
			code = roomCode()
		}
		token := key(24)
		b.rooms[code] = &room{owner: token, source: source, open: true, expires: now.Add(roomLease)}
		return response{Code: code, Token: token}, http.StatusOK
	}

	if operation == "join" && !b.allow(source, false, now) {
		return response{Error: "busy"}, http.StatusTooManyRequests
	}
	r := b.rooms[req.Code]
	if r == nil || !r.expires.After(now) {
		delete(b.rooms, req.Code)
		return response{Error: "room_closed"}, http.StatusNotFound
	}
	r.expireAttempts(now)
	switch operation {
	case "join":
		if !r.open || len(r.waiting) >= queueLimit {
			return response{Error: "room_busy"}, http.StatusConflict
		}
		a := &attempt{id: key(12), token: key(24), expires: now.Add(joinLease)}
		r.waiting = append(r.waiting, a)
		return response{Token: a.token}, http.StatusOK
	case "host", "close":
		if !sameToken(r.owner, token) {
			return response{Error: "room_closed"}, http.StatusNotFound
		}
		if operation == "close" {
			delete(b.rooms, req.Code)
			return response{}, http.StatusOK
		}
		r.expires, r.open = now.Add(roomLease), req.Open
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

func (r *room) expireAttempts(now time.Time) {
	kept := r.waiting[:0]
	for _, a := range r.waiting {
		if a.expires.After(now) {
			kept = append(kept, a)
		}
	}
	clear(r.waiting[len(kept):])
	r.waiting = kept
}

func (r *room) remove(i int) {
	last := len(r.waiting) - 1
	copy(r.waiting[i:], r.waiting[i+1:])
	r.waiting[last] = nil
	r.waiting = r.waiting[:last]
}

func (r *room) host(req request) (response, int) {
	if len(r.waiting) == 0 {
		if req.Attempt != "" && !req.Finish {
			return response{Error: "attempt_closed"}, http.StatusNotFound
		}
		return response{}, http.StatusOK
	}
	a := r.waiting[0]
	if req.Finish {
		if req.Attempt == a.id {
			r.remove(0)
		}
		return response{}, http.StatusOK
	}
	if req.Attempt == "" {
		if r.open {
			return response{Attempt: a.id}, http.StatusOK
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
			r.remove(i)
			return response{}, http.StatusOK
		}
		if req.Reply != "" {
			if a.offer == "" || a.reply != "" && a.reply != req.Reply {
				return response{Error: "invalid_request"}, http.StatusConflict
			}
			a.reply = req.Reply
			return response{}, http.StatusOK
		}
		return response{Offer: a.offer}, http.StatusOK
	}
	return response{Error: "attempt_closed"}, http.StatusNotFound
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
