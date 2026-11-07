#pragma once
#include <stdint.h>
#ifdef MOON_ICE_EXPORTS
#define MICE_API __declspec(dllexport)
#else
#define MICE_API __declspec(dllimport)
#endif

// All calls on a context must be serialized by its owner. Callbacks never enter
// managed code. Destroy joins the native workers before releasing their storage.
// IDs are local handles, not authentication. Only DTLS authenticates game data.
struct mice_context;
struct mice_stats {
    uint64_t received_packets, received_bytes, sent_packets, sent_bytes;
    uint64_t dropped_packets, send_errors, queued_packets;
};
extern "C" {
MICE_API int mice_abi_version();
MICE_API mice_context* mice_create(int queue_capacity);
MICE_API void mice_destroy(mice_context* context);
MICE_API int mice_add_peer(mice_context* context, const char* bind_address,
                          const char* stun_host, uint16_t stun_port);
MICE_API int mice_remove_peer(mice_context* context, int peer);
MICE_API int mice_gather(mice_context* context, int peer);
MICE_API int mice_gathered(mice_context* context, int peer);
MICE_API int mice_state(mice_context* context, int peer);
MICE_API int mice_description(mice_context* context, int peer, char* buffer, int capacity);
MICE_API int mice_remote_description(mice_context* context, int peer, const char* description);
MICE_API int mice_send(mice_context* context, int peer, const void* buffer, int length);
// Returns length, zero if empty, or a negative error. Max datagram is 1472 bytes.
MICE_API int mice_receive(mice_context* context, int* peer, void* buffer, int capacity);
MICE_API int mice_get_stats(mice_context* context, mice_stats* stats);
}
