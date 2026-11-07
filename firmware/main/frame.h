/*
 * Frame layer of the CodeBlue binary protocol (docs/PROTOCOL.md):
 * CRC-16/CCITT-FALSE plus COBS, with a 0x00 delimiter after each frame.
 *
 * Plain C with no ESP-IDF dependencies, so it can be tested on a PC.
 *
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* Longest encoded frame a receiver accepts, not counting the delimiter. */
#define FRAME_MAX_ENCODED 64
/* Longest payload (the bytes after the type). */
#define FRAME_MAX_PAYLOAD 60
/* Longest decoded frame: type + payload + CRC. */
#define FRAME_MAX_DECODED (1 + FRAME_MAX_PAYLOAD + 2)
/* Buffer size that always holds an encoded frame plus its delimiter. */
#define FRAME_MAX_WIRE (FRAME_MAX_DECODED + FRAME_MAX_DECODED / 254 + 2)

uint16_t frame_crc16(const uint8_t *data, size_t len);

/*
 * Builds a complete frame (COBS-encoded, with the trailing 0x00) in `out`,
 * which must hold FRAME_MAX_WIRE bytes. Returns the number of bytes to send,
 * or 0 if the payload is too long.
 */
size_t frame_encode(uint8_t type, const uint8_t *payload, size_t payload_len, uint8_t *out);

/* Incremental receiver: feed it bytes as they arrive. */
typedef struct {
    uint8_t buf[FRAME_MAX_ENCODED];
    size_t len;
    bool overflow; /* current frame is too long; discard until the next 0x00 */
} frame_rx_t;

void frame_rx_init(frame_rx_t *rx);

/*
 * Feeds one received byte. When it completes a valid frame (good COBS, good
 * CRC), returns true and sets *type, copies the payload into `payload`
 * (FRAME_MAX_PAYLOAD bytes) and sets *payload_len. Invalid frames are
 * dropped silently.
 */
bool frame_rx_byte(frame_rx_t *rx, uint8_t byte, uint8_t *type, uint8_t *payload, size_t *payload_len);

#ifdef __cplusplus
}
#endif
