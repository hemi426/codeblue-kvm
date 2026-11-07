/*
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#include "session.h"

#include <stdio.h>
#include <string.h>

#include "driver/uart.h"
#include "esp_app_desc.h"
#include "esp_log.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

#include "frame.h"
#include "status_led.h"
#include "usb_hid.h"

static const char *TAG = "session";

#define UART_PORT UART_NUM_0
#define UART_BAUD 921600

#define PROTOCOL_MAJOR 1
#define PROTOCOL_MINOR 0

#define HEARTBEAT_MS 100
#define STATUS_MS 1000
#define DEFAULT_TIMEOUT_MS 500

/* Capability bits in INFO. */
#define CAP_ABS_MOUSE  (1u << 0)
#define CAP_REL_MOUSE  (1u << 1)
#define CAP_PERSISTENT (1u << 2)

enum {
    MSG_HELLO = 0x01,
    MSG_HEARTBEAT = 0x02,
    MSG_SESSION_CONFIG = 0x03,
    MSG_KEYBOARD = 0x10,
    MSG_MOUSE_ABS = 0x20,
    MSG_MOUSE_REL = 0x21,
    MSG_LED = 0x30,
    MSG_LED_BRIGHTNESS = 0x31,
    MSG_RELEASE_ALL = 0x40,

    MSG_INFO = 0x81,
    MSG_DEV_HEARTBEAT = 0x82,
    MSG_TARGET_STATUS = 0x90,
    MSG_ERROR = 0xE0,
};

enum { ERR_UNKNOWN_TYPE = 1, ERR_BAD_LENGTH = 2 };

static bool s_active;
static uint32_t s_timeout_ms = DEFAULT_TIMEOUT_MS;
static int64_t s_last_rx_ms, s_last_tx_ms, s_last_status_ms;
static uint8_t s_last_flags = 0xFF, s_last_leds = 0xFF;

static int64_t now_ms(void)
{
    return esp_timer_get_time() / 1000;
}

static void send(uint8_t type, const uint8_t *payload, size_t len)
{
    uint8_t wire[FRAME_MAX_WIRE];
    size_t n = frame_encode(type, payload, len, wire);
    if (n) {
        uart_write_bytes(UART_PORT, wire, n);
        s_last_tx_ms = now_ms();
    }
}

static void send_error(uint8_t code, uint8_t detail)
{
    const uint8_t p[2] = { code, detail };
    send(MSG_ERROR, p, sizeof p);
}

static void send_info(void)
{
    unsigned major = 0, minor = 0, patch = 0;
    sscanf(esp_app_get_description()->version, "%u.%u.%u", &major, &minor, &patch);
    const uint16_t caps = CAP_ABS_MOUSE | CAP_REL_MOUSE | CAP_PERSISTENT;

    uint8_t p[FRAME_MAX_PAYLOAD];
    size_t n = 0;
    p[n++] = PROTOCOL_MAJOR;
    p[n++] = PROTOCOL_MINOR;
    p[n++] = (uint8_t)major;
    p[n++] = (uint8_t)minor;
    p[n++] = (uint8_t)patch;
    p[n++] = (uint8_t)(caps & 0xFF);
    p[n++] = (uint8_t)(caps >> 8);
    p[n++] = status_led_brightness();
    const char *board = CONFIG_CODEBLUE_BOARD_NAME;
    size_t board_len = strlen(board);
    if (board_len > sizeof p - n)
        board_len = sizeof p - n;
    memcpy(&p[n], board, board_len);
    n += board_len;
    send(MSG_INFO, p, n);
}

static void send_status(uint8_t flags, uint8_t leds)
{
    const uint8_t p[2] = { flags, leds };
    send(MSG_TARGET_STATUS, p, sizeof p);
    s_last_flags = flags;
    s_last_leds = leds;
    s_last_status_ms = now_ms();
}

static void end_session(const char *why)
{
    ESP_LOGI(TAG, "Session ended: %s", why);
    s_active = false;
    usb_hid_release_all();
    status_led_set(LED_NO_HOST);
}

static void on_hello(const uint8_t *p, size_t len)
{
    if (len < 5 || memcmp(p, "CBLU", 4) != 0)
        return; /* not for us */

    /* New or restarted host: start from a clean slate. */
    usb_hid_release_all();
    s_timeout_ms = DEFAULT_TIMEOUT_MS;
    send_info();

    /* Always answer, so the host can name both versions; but only a host
     * speaking our major version gets a session. */
    s_active = (p[4] == PROTOCOL_MAJOR);
    if (!s_active)
        return;
    s_last_rx_ms = now_ms();
    send_status(usb_hid_status(), usb_hid_kbd_leds());
    status_led_set(LED_HOST);
}

/* Wake a sleeping target on a press, as a real keyboard would. */
static void wake_if_pressed(bool pressed)
{
    if (pressed && (usb_hid_status() & TS_SUSPENDED))
        usb_hid_wake();
}

static void handle(uint8_t type, const uint8_t *p, size_t len)
{
    if (type == MSG_HELLO) {
        on_hello(p, len);
        return;
    }
    if (!s_active)
        return; /* nothing but HELLO counts until a session starts */
    s_last_rx_ms = now_ms();

#define NEED(n)                              \
    do {                                     \
        if (len < (n)) {                     \
            send_error(ERR_BAD_LENGTH, type); \
            return;                          \
        }                                    \
    } while (0)

    switch (type) {
    case MSG_HEARTBEAT:
        break;

    case MSG_SESSION_CONFIG: {
        NEED(2);
        uint32_t t = (uint32_t)(p[0] | (p[1] << 8));
        s_timeout_ms = t < 100 ? 100 : t > 60000 ? 60000 : t;
        break;
    }

    case MSG_KEYBOARD: {
        NEED(2);
        size_t count = p[1] > 6 ? 6 : p[1];
        NEED(2 + count);
        uint8_t keys[6] = { 0 };
        memcpy(keys, &p[2], count);
        wake_if_pressed(p[0] != 0 || count != 0);
        usb_hid_keyboard(p[0], keys);
        break;
    }

    case MSG_MOUSE_ABS: {
        NEED(7);
        uint16_t x = (uint16_t)(p[0] | (p[1] << 8));
        uint16_t y = (uint16_t)(p[2] | (p[3] << 8));
        wake_if_pressed(p[4] != 0);
        usb_hid_mouse_abs(x, y, p[4], (int8_t)p[5], (int8_t)p[6]);
        break;
    }

    case MSG_MOUSE_REL: {
        NEED(7);
        int16_t dx = (int16_t)(p[0] | (p[1] << 8));
        int16_t dy = (int16_t)(p[2] | (p[3] << 8));
        wake_if_pressed(p[4] != 0);
        usb_hid_mouse_rel(p[4], dx, dy, (int8_t)p[5], (int8_t)p[6]);
        break;
    }

    case MSG_LED:
        NEED(3);
        status_led_set(p[0], p[1], p[2]);
        break;

    case MSG_LED_BRIGHTNESS:
        NEED(2);
        status_led_set_brightness(p[0], p[1] == 1);
        break;

    case MSG_RELEASE_ALL:
        usb_hid_release_all();
        break;

    default:
        send_error(ERR_UNKNOWN_TYPE, type);
        break;
    }
#undef NEED
}

static void session_task(void *arg)
{
    (void)arg;
    frame_rx_t rx;
    frame_rx_init(&rx);
    uint8_t buf[128];
    uint8_t payload[FRAME_MAX_PAYLOAD];

    for (;;) {
        int n = uart_read_bytes(UART_PORT, buf, sizeof buf, pdMS_TO_TICKS(2));
        for (int i = 0; i < n; i++) {
            uint8_t type;
            size_t len;
            if (frame_rx_byte(&rx, buf[i], &type, payload, &len))
                handle(type, payload, len);
        }

        usb_hid_poll();

        if (!s_active)
            continue;

        int64_t now = now_ms();
        if (now - s_last_rx_ms > (int64_t)s_timeout_ms) {
            end_session("host went quiet");
            continue;
        }

        uint8_t flags = usb_hid_status(), leds = usb_hid_kbd_leds();
        if (flags != s_last_flags || leds != s_last_leds || now - s_last_status_ms >= STATUS_MS)
            send_status(flags, leds);
        else if (now - s_last_tx_ms >= HEARTBEAT_MS)
            send(MSG_DEV_HEARTBEAT, NULL, 0);
    }
}

void session_start(void)
{
    ESP_LOGI(TAG, "Host protocol on UART0 at %d baud.", UART_BAUD);
#if !CONFIG_CODEBLUE_UART_LOGS
    /* Log output would land in the middle of protocol frames. */
    ESP_LOGI(TAG, "Logging off from here on (CONFIG_CODEBLUE_UART_LOGS).");
    esp_log_level_set("*", ESP_LOG_NONE);
#endif

    const uart_config_t cfg = {
        .baud_rate = UART_BAUD,
        .data_bits = UART_DATA_8_BITS,
        .parity = UART_PARITY_DISABLE,
        .stop_bits = UART_STOP_BITS_1,
        .flow_ctrl = UART_HW_FLOWCTRL_DISABLE,
        .source_clk = UART_SCLK_DEFAULT,
    };
    ESP_ERROR_CHECK(uart_driver_install(UART_PORT, 1024, 1024, 0, NULL, 0));
    ESP_ERROR_CHECK(uart_param_config(UART_PORT, &cfg));

    xTaskCreate(session_task, "session", 4096, NULL, 6, NULL);
}
