/*
 * Tests for the frame layer (firmware/main/frame.c), run on a PC:
 *
 *   gcc -std=c11 -Wall -Wextra -Werror -Ifirmware/main \
 *       firmware/test/test_frame.c firmware/main/frame.c -o build/test_frame
 *   ./build/test_frame
 *
 * The expected bytes are the worked examples in docs/PROTOCOL.md; if one of
 * these fails, either the code or the spec is wrong.
 *
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#include <stdio.h>
#include <string.h>

#include "frame.h"

static int failures;

#define CHECK(cond, ...)                \
    do {                                \
        if (!(cond)) {                  \
            printf("FAIL: " __VA_ARGS__); \
            printf("\n");               \
            failures++;                 \
        }                               \
    } while (0)

static void check_encode(const char *name, uint8_t type, const uint8_t *payload, size_t len,
                         const uint8_t *expected, size_t expected_len)
{
    uint8_t out[FRAME_MAX_WIRE];
    size_t n = frame_encode(type, payload, len, out);
    CHECK(n == expected_len && memcmp(out, expected, n) == 0, "%s encodes differently from docs/PROTOCOL.md", name);
}

/* Feeds bytes to a receiver; returns how many valid frames came out, and the last one. */
static int feed(frame_rx_t *rx, const uint8_t *bytes, size_t len, uint8_t *type, uint8_t *payload, size_t *payload_len)
{
    int frames = 0;
    for (size_t i = 0; i < len; i++)
        if (frame_rx_byte(rx, bytes[i], type, payload, payload_len))
            frames++;
    return frames;
}

int main(void)
{
    /* CRC-16/CCITT-FALSE check value. */
    CHECK(frame_crc16((const uint8_t *)"123456789", 9) == 0x29B1, "CRC check value");

    /* Worked examples from docs/PROTOCOL.md. */
    const uint8_t ctrl_c[] = { 0x01, 0x01, 0x06 };
    const uint8_t ctrl_c_wire[] = { 0x07, 0x10, 0x01, 0x01, 0x06, 0xA0, 0xFB, 0x00 };
    check_encode("Ctrl+C", 0x10, ctrl_c, sizeof ctrl_c, ctrl_c_wire, sizeof ctrl_c_wire);

    const uint8_t release[] = { 0x00, 0x00 };
    const uint8_t release_wire[] = { 0x02, 0x10, 0x01, 0x03, 0xFF, 0x8F, 0x00 };
    check_encode("release", 0x10, release, sizeof release, release_wire, sizeof release_wire);

    const uint8_t hello[] = { 'C', 'B', 'L', 'U', 0x01 };
    const uint8_t hello_wire[] = { 0x09, 0x01, 0x43, 0x42, 0x4C, 0x55, 0x01, 0x59, 0xBE, 0x00 };
    check_encode("HELLO", 0x01, hello, sizeof hello, hello_wire, sizeof hello_wire);

    /* Every payload length round-trips, zeros included, within the 64-byte limit. */
    for (size_t len = 0; len <= FRAME_MAX_PAYLOAD; len++) {
        uint8_t payload[FRAME_MAX_PAYLOAD], wire[FRAME_MAX_WIRE], got[FRAME_MAX_PAYLOAD];
        for (size_t i = 0; i < len; i++)
            payload[i] = (uint8_t)(i % 3 ? i * 7 : 0);
        size_t n = frame_encode(0x20, payload, len, wire);
        CHECK(n - 1 <= FRAME_MAX_ENCODED, "payload of %zu bytes encodes to %zu, over the limit", len, n - 1);

        frame_rx_t rx;
        frame_rx_init(&rx);
        uint8_t type = 0;
        size_t got_len = 0;
        int frames = feed(&rx, wire, n, &type, got, &got_len);
        CHECK(frames == 1 && type == 0x20 && got_len == len && memcmp(got, payload, len) == 0,
              "payload of %zu bytes doesn't round-trip", len);
    }

    /* The receiver recovers from what a real link throws at it. */
    {
        frame_rx_t rx;
        frame_rx_init(&rx);
        uint8_t type, payload[FRAME_MAX_PAYLOAD];
        size_t len;

        /* Boot-log text before the first delimiter swallows the first frame... */
        const char *boot = "ESP-ROM:esp32s3-20210327\r\nrst:0x1 (POWERON)\r\n";
        int frames = feed(&rx, (const uint8_t *)boot, strlen(boot), &type, payload, &len);
        frames += feed(&rx, ctrl_c_wire, sizeof ctrl_c_wire, &type, payload, &len);
        CHECK(frames == 0, "a frame glued to boot-log text was accepted");
        /* ...and the next one gets through. */
        CHECK(feed(&rx, ctrl_c_wire, sizeof ctrl_c_wire, &type, payload, &len) == 1, "no recovery after boot-log text");

        /* A corrupted frame is dropped. */
        uint8_t bad[sizeof release_wire];
        memcpy(bad, release_wire, sizeof bad);
        bad[3] ^= 0x40;
        CHECK(feed(&rx, bad, sizeof bad, &type, payload, &len) == 0, "a corrupted frame was accepted");

        /* An oversized frame is dropped, and the receiver carries on. */
        uint8_t big[100];
        memset(big, 0x55, sizeof big);
        int got = feed(&rx, big, sizeof big, &type, payload, &len);
        got += feed(&rx, (const uint8_t[]){ 0x00 }, 1, &type, payload, &len);
        CHECK(got == 0, "an oversized frame was accepted");
        CHECK(feed(&rx, hello_wire, sizeof hello_wire, &type, payload, &len) == 1 && type == 0x01 &&
              len == sizeof hello && memcmp(payload, hello, len) == 0,
              "no recovery after an oversized frame");

        /* Stray delimiters (empty frames) are ignored. */
        const uint8_t zeros[] = { 0x00, 0x00, 0x00 };
        CHECK(feed(&rx, zeros, sizeof zeros, &type, payload, &len) == 0, "an empty frame was accepted");
    }

    if (failures) {
        printf("%d failure(s)\n", failures);
        return 1;
    }
    printf("frame tests passed\n");
    return 0;
}
