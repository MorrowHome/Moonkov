#include "moon_ice.h"
#include <array>
#include <chrono>
#include <cstdio>
#include <memory>
#include <stdexcept>
#include <thread>

namespace {
int checks = 0;
void require(bool condition, const char* label) {
    if (!condition) throw std::runtime_error(label);
    ++checks;
}
template<class F> void wait(F predicate, const char* label) {
    const auto until = std::chrono::steady_clock::now() + std::chrono::seconds(5);
    while (!predicate()) {
        if (std::chrono::steady_clock::now() > until) throw std::runtime_error(label);
        std::this_thread::sleep_for(std::chrono::milliseconds(2));
    }
    ++checks;
}
using Context = std::unique_ptr<mice_context, decltype(&mice_destroy)>;
Context context() { return Context(mice_create(8), mice_destroy); }
void connect(mice_context* a, int pa, mice_context* b, int pb) {
    require(mice_gather(a, pa) == 0 && mice_gather(b, pb) == 0, "gather");
    wait([&] { return mice_gathered(a, pa) == 1 && mice_gathered(b, pb) == 1; }, "gather timeout");
    char sa[4096]{}, sb[4096]{};
    require(mice_description(a, pa, sa, sizeof(sa)) == 0 && mice_description(b, pb, sb, sizeof(sb)) == 0, "SDP");
    require(mice_remote_description(a, pa, sb) == 0 && mice_remote_description(b, pb, sa) == 0, "remote SDP");
    wait([&] { const int x = mice_state(a, pa), y = mice_state(b, pb); return x >= 3 && x <= 4 && y >= 3 && y <= 4; }, "ICE timeout");
}
void transfer(mice_context* a, int pa, mice_context* b, int pb, int length) {
    std::array<unsigned char, 1472> sent{}, got{};
    for (int i = 0; i < length; ++i) sent[i] = static_cast<unsigned char>(i * 37);
    require(mice_send(a, pa, sent.data(), length) == 0, "send");
    int peer = 0, size = 0;
    wait([&] { size = mice_receive(b, &peer, got.data(), static_cast<int>(got.size())); return size != 0; }, "receive timeout");
    require(size == length && peer == pb && got == sent, "payload and peer identity");
}
}
int main() {
    try {
        require(mice_abi_version() == 1 && mice_create(0) == nullptr, "ABI / capacity guard");
        auto server = context(), a = context(), b = context();
        require(server && a && b, "context allocation");
        const int sa = mice_add_peer(server.get(), "127.0.0.1", nullptr, 0);
        const int sb = mice_add_peer(server.get(), "127.0.0.1", nullptr, 0);
        const int ca = mice_add_peer(a.get(), "127.0.0.1", nullptr, 0);
        const int cb = mice_add_peer(b.get(), "127.0.0.1", nullptr, 0);
        require(sa > 0 && sb > 0 && sa != sb && ca > 0 && cb > 0, "multiple peer handles");
        connect(server.get(), sa, a.get(), ca);
        connect(server.get(), sb, b.get(), cb);
        transfer(a.get(), ca, server.get(), sa, 64);
        transfer(server.get(), sa, a.get(), ca, 1200);
        transfer(b.get(), cb, server.get(), sb, 1472);
        transfer(server.get(), sb, b.get(), cb, 64);
        char data[1473]{};
        require(mice_send(a.get(), ca, data, sizeof(data)) == -6, "oversize guard");
        for (int i = 0; i < 32; ++i) require(mice_send(a.get(), ca, data, 64) == 0, "burst send");
        mice_stats stats{};
        wait([&] { mice_get_stats(server.get(), &stats); return stats.received_packets >= 34; }, "burst arrival");
        require(stats.queued_packets == 8 && stats.dropped_packets == 24, "bounded queue and drop counter");
        require(mice_remove_peer(server.get(), sa) == 0, "remove active peer");
        int peer = 0;
        require(mice_receive(server.get(), &peer, data, sizeof(data)) == 0, "discard removed peer packets");
        const int replacement = mice_add_peer(server.get(), "127.0.0.1", nullptr, 0);
        require(replacement > sb && mice_state(server.get(), sa) == -1, "no peer ID reuse");
        // Destroy while the replacement is still gathering; then create again.
        require(mice_gather(server.get(), replacement) == 0, "gather before cancellation");
        server.reset(); a.reset(); b.reset();
        auto fresh = context();
        require(fresh && mice_add_peer(fresh.get(), "127.0.0.1", nullptr, 0) > 0, "recreate after disposal");
        std::printf("{\"passed\":%d,\"scope\":\"isolated native datagrams; no public network\"}\n", checks);
        return 0;
    } catch (const std::exception& e) { std::fprintf(stderr, "FAILED: %s\n", e.what()); return 1; }
}
