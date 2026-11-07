#include "moon_ice.h"
#include <juice/juice.h>
#include <array>
#include <atomic>
#include <climits>
#include <cstring>
#include <memory>
#include <mutex>
#include <vector>

namespace {
constexpr int MaxDatagram = 1472;
constexpr int MaxPeers = 64;
struct Packet { int peer = 0, length = 0; char data[MaxDatagram]{}; };
struct Peer {
    mice_context* owner = nullptr;
    int id = 0;
    juice_agent_t* agent = nullptr;
    std::atomic<bool> gathered{false};
    ~Peer() { if (agent) juice_destroy(agent); }
};
}

struct mice_context {
    std::array<std::unique_ptr<Peer>, MaxPeers> peers{};
    std::vector<Packet> queue;
    std::mutex queue_mutex;
    size_t head = 0, count = 0;
    int next_id = 1;
    mice_stats stats{};
    explicit mice_context(int capacity) : queue(static_cast<size_t>(capacity)) {}
    ~mice_context() {
        // Peer callbacks require queue + mutex, which must outlive every agent.
        for (auto& peer : peers) peer.reset();
    }
};

namespace {
Peer* find(mice_context* c, int id) {
    if (c && id > 0) for (auto& p : c->peers) if (p && p->id == id) return p.get();
    return nullptr;
}
void on_gathered(juice_agent_t*, void* user) {
    static_cast<Peer*>(user)->gathered.store(true);
}
void on_receive(juice_agent_t*, const char* data, size_t length, void* user) {
    auto* peer = static_cast<Peer*>(user);
    auto& c = *peer->owner;
    std::lock_guard<std::mutex> guard(c.queue_mutex);
    ++c.stats.received_packets;
    c.stats.received_bytes += length;
    if (length == 0 || length > MaxDatagram || c.count == c.queue.size()) {
        ++c.stats.dropped_packets;
        return;
    }
    auto& packet = c.queue[(c.head + c.count) % c.queue.size()];
    packet.peer = peer->id;
    packet.length = static_cast<int>(length);
    std::memcpy(packet.data, data, length);
    ++c.count;
}
}

int mice_abi_version() { return 1; }
mice_context* mice_create(int capacity) {
    if (capacity < 8 || capacity > 4096) return nullptr;
    try { return new mice_context(capacity); } catch (...) { return nullptr; }
}
void mice_destroy(mice_context* c) { delete c; }
int mice_add_peer(mice_context* c, const char* bind, const char* stun, uint16_t port) {
    if (!c || c->next_id == INT_MAX) return JUICE_ERR_INVALID;
    for (auto& slot : c->peers) {
        if (slot) continue;
        try {
            auto peer = std::make_unique<Peer>();
            peer->owner = c;
            peer->id = c->next_id++;
            juice_config_t config{};
            // One shared poll worker, not one worker thread per player.
            config.concurrency_mode = JUICE_CONCURRENCY_MODE_POLL;
            config.bind_address = bind && *bind ? bind : nullptr;
            config.stun_server_host = stun && *stun ? stun : nullptr;
            config.stun_server_port = port;
            config.cb_gathering_done = on_gathered;
            config.cb_recv = on_receive;
            config.user_ptr = peer.get();
            peer->agent = juice_create(&config);
            if (!peer->agent) return JUICE_ERR_FAILED;
            const int id = peer->id;
            slot = std::move(peer);
            return id;
        } catch (...) { return JUICE_ERR_FAILED; }
    }
    return JUICE_ERR_NOT_AVAIL;
}
int mice_remove_peer(mice_context* c, int id) {
    if (!c) return JUICE_ERR_INVALID;
    for (auto& p : c->peers) if (p && p->id == id) { p.reset(); return 0; }
    return JUICE_ERR_INVALID;
}
int mice_gather(mice_context* c, int id) {
    auto* p = find(c, id); return p ? juice_gather_candidates(p->agent) : JUICE_ERR_INVALID;
}
int mice_gathered(mice_context* c, int id) {
    auto* p = find(c, id); return p ? (p->gathered.load() ? 1 : 0) : JUICE_ERR_INVALID;
}
int mice_state(mice_context* c, int id) {
    auto* p = find(c, id); return p ? static_cast<int>(juice_get_state(p->agent)) : JUICE_ERR_INVALID;
}
int mice_description(mice_context* c, int id, char* buffer, int capacity) {
    auto* p = find(c, id);
    if (!p || !buffer || capacity <= 0) return JUICE_ERR_INVALID;
    return juice_get_local_description(p->agent, buffer, static_cast<size_t>(capacity));
}
int mice_remote_description(mice_context* c, int id, const char* description) {
    auto* p = find(c, id);
    if (!p || !description || strnlen(description, JUICE_MAX_SDP_STRING_LEN) >= JUICE_MAX_SDP_STRING_LEN)
        return JUICE_ERR_INVALID;
    const int result = juice_set_remote_description(p->agent, description);
    // This prototype exchanges complete descriptions, not trickled candidates.
    return result == 0 ? juice_set_remote_gathering_done(p->agent) : result;
}
int mice_send(mice_context* c, int id, const void* buffer, int length) {
    auto* p = find(c, id);
    if (!p || !buffer || length <= 0) return JUICE_ERR_INVALID;
    if (length > MaxDatagram) return JUICE_ERR_TOO_LARGE;
    const int result = juice_send(p->agent, static_cast<const char*>(buffer), static_cast<size_t>(length));
    std::lock_guard<std::mutex> guard(c->queue_mutex);
    if (result == 0) { ++c->stats.sent_packets; c->stats.sent_bytes += length; }
    else ++c->stats.send_errors;
    return result;
}
int mice_receive(mice_context* c, int* id, void* buffer, int capacity) {
    if (!c || !id || !buffer || capacity <= 0) return JUICE_ERR_INVALID;
    std::lock_guard<std::mutex> guard(c->queue_mutex);
    while (c->count) {
        const auto& p = c->queue[c->head];
        if (find(c, p.peer)) {
            if (capacity < p.length) return JUICE_ERR_TOO_LARGE;
            std::memcpy(buffer, p.data, static_cast<size_t>(p.length));
            *id = p.peer;
            c->head = (c->head + 1) % c->queue.size();
            --c->count;
            return p.length;
        }
        // Removed peer IDs are never reused; discard their delayed datagrams.
        c->head = (c->head + 1) % c->queue.size();
        --c->count;
        ++c->stats.dropped_packets;
    }
    return 0;
}
int mice_get_stats(mice_context* c, mice_stats* stats) {
    if (!c || !stats) return JUICE_ERR_INVALID;
    std::lock_guard<std::mutex> guard(c->queue_mutex);
    *stats = c->stats;
    stats->queued_packets = c->count;
    return 0;
}
