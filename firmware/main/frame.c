/*
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#include "frame.h"

#include <string.h>

uint16_t frame_crc16(const uint8_t *data, size_t len)
{
    uint16_t crc = 0xFFFF;
    for (size_t i = 0; i < len; i++) {
        crc ^= (uint16_t)data[i] << 8;
        for (int bit = 0; bit < 8; bit++)
            crc = (crc & 0x8000) ? (uint16_t)((crc << 1) ^ 0x1021) : (uint16_t)(crc << 1);
    }
    return crc;
}

/* COBS-encodes `len` bytes into `out`; returns the encoded length (no delimiter). */
static size_t cobs_encode(const uint8_t *in, size_t len, uint8_t *out)
{
    size_t code_at = 0, o = 1;
    uint8_t code = 1;
    for (size_t i = 0; i < len; i++) {
        if (in[i] == 0) {
            out[code_at] = code;
            code_at = o++;
            code = 1;
        } else {
            out[o++] = in[i];
            if (++code == 0xFF) {
                out[code_at] = code;
                code_at = o++;
                code = 1;
            }
        }
    }
    out[code_at] = code;
    return o;
}

/* Decodes `len` bytes from `in` into `out`; returns the decoded length, or -1 if malformed. */
static int cobs_decode(const uint8_t *in, size_t len, uint8_t *out)
{
    size_t i = 0, o = 0;
    while (i < len) {
        uint8_t code = in[i++];
        if (code == 0 || i + code - 1 > len)
            return -1;
        for (uint8_t k = 1; k < code; k++)
            out[o++] = in[i++];
        if (code != 0xFF && i < len)
            out[o++] = 0;
    }
    return (int)o;
}

size_t frame_encode(uint8_t type, const uint8_t *payload, size_t payload_len, uint8_t *out)
{
    if (payload_len > FRAME_MAX_PAYLOAD)
        return 0;
    uint8_t raw[FRAME_MAX_DECODED];
    raw[0] = type;
    if (payload_len)
        memcpy(&raw[1], payload, payload_len);
    uint16_t crc = frame_crc16(raw, 1 + payload_len);
    raw[1 + payload_len] = (uint8_t)(crc & 0xFF);
    raw[2 + payload_len] = (uint8_t)(crc >> 8);
    size_t n = cobs_encode(raw, 3 + payload_len, out);
    out[n++] = 0x00;
    return n;
}

void frame_rx_init(frame_rx_t *rx)
{
    rx->len = 0;
    rx->overflow = false;
}

bool frame_rx_byte(frame_rx_t *rx, uint8_t byte, uint8_t *type, uint8_t *payload, size_t *payload_len)
{
    if (byte != 0x00) {
        if (rx->len < sizeof rx->buf)
            rx->buf[rx->len++] = byte;
        else
            rx->overflow = true;
        return false;
    }

    /* Delimiter: try to decode what we have, then start over. */
    bool ok = false;
    if (!rx->overflow && rx->len > 0) {
        uint8_t raw[FRAME_MAX_ENCODED];
        int n = cobs_decode(rx->buf, rx->len, raw);
        if (n >= 3 && n <= FRAME_MAX_DECODED) {
            uint16_t got = (uint16_t)(raw[n - 2] | (raw[n - 1] << 8));
            if (frame_crc16(raw, (size_t)n - 2) == got) {
                *type = raw[0];
                *payload_len = (size_t)n - 3;
                memcpy(payload, &raw[1], *payload_len);
                ok = true;
            }
        }
    }
    frame_rx_init(rx);
    return ok;
}
