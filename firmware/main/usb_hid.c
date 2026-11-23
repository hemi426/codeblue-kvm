/*
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#include "usb_hid.h"

#include <stdio.h>
#include <string.h>

#include "driver/gpio.h"
#include "esp_check.h"
#include "esp_log.h"
#include "esp_mac.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"
#include "tinyusb.h"
#include "tinyusb_default_config.h"
#include "tusb.h"

#include "settings.h"

static const char *TAG = "usb";

/* ---- Descriptors ------------------------------------------------------- */

enum { ITF_KBD, ITF_MOUSE, ITF_ABS, ITF_COUNT };
enum { STR_LANG, STR_MANUFACTURER, STR_PRODUCT, STR_SERIAL, STR_KBD, STR_MOUSE, STR_ABS, STR_COUNT };

#define EP_KBD   0x81
#define EP_MOUSE 0x82
#define EP_ABS   0x83
#define EP_SIZE  8
#define EP_INTERVAL_MS 1

static const uint8_t s_report_kbd[] = { TUD_HID_REPORT_DESC_KEYBOARD() };
static const uint8_t s_report_mouse[] = { TUD_HID_REPORT_DESC_MOUSE() };
static const uint8_t s_report_abs[] = { TUD_HID_REPORT_DESC_ABSMOUSE() };

#define CONFIG_TOTAL_LEN (TUD_CONFIG_DESC_LEN + ITF_COUNT * TUD_HID_DESC_LEN)

static const uint8_t s_config_desc[] = {
    TUD_CONFIG_DESCRIPTOR(1, ITF_COUNT, 0, CONFIG_TOTAL_LEN, TUSB_DESC_CONFIG_ATT_REMOTE_WAKEUP, 100),
    TUD_HID_DESCRIPTOR(ITF_KBD, STR_KBD, HID_ITF_PROTOCOL_KEYBOARD, sizeof(s_report_kbd), EP_KBD, EP_SIZE, EP_INTERVAL_MS),
    TUD_HID_DESCRIPTOR(ITF_MOUSE, STR_MOUSE, HID_ITF_PROTOCOL_MOUSE, sizeof(s_report_mouse), EP_MOUSE, EP_SIZE, EP_INTERVAL_MS),
    TUD_HID_DESCRIPTOR(ITF_ABS, STR_ABS, HID_ITF_PROTOCOL_NONE, sizeof(s_report_abs), EP_ABS, EP_SIZE, EP_INTERVAL_MS),
};

static const tusb_desc_device_t s_device_desc = {
    .bLength = sizeof(tusb_desc_device_t),
    .bDescriptorType = TUSB_DESC_DEVICE,
    .bcdUSB = 0x0200,
    .bDeviceClass = 0x00, /* defined per interface */
    .bDeviceSubClass = 0x00,
    .bDeviceProtocol = 0x00,
    .bMaxPacketSize0 = CFG_TUD_ENDPOINT0_SIZE,
    .idVendor = CONFIG_CODEBLUE_USB_VID,
    .idProduct = CONFIG_CODEBLUE_USB_PID,
    .bcdDevice = 0x0100,
    .iManufacturer = STR_MANUFACTURER,
    .iProduct = STR_PRODUCT,
    .iSerialNumber = STR_SERIAL,
    .bNumConfigurations = 1,
};

static char s_serial[13]; /* the chip's MAC, so two boards on one target stay distinct */

static const char *s_strings[STR_COUNT] = {
    [STR_LANG] = (const char[]){ 0x09, 0x04 }, /* English (US) */
    [STR_MANUFACTURER] = "CodeBlue",
    [STR_PRODUCT] = "CodeBlue KVM",
    [STR_SERIAL] = s_serial,
    [STR_KBD] = "CodeBlue Keyboard",
    [STR_MOUSE] = "CodeBlue Mouse",
    [STR_ABS] = "CodeBlue Absolute Pointer",
};

/* ---- State --------------------------------------------------------------
 * The session task writes the wanted state; usb_hid_poll() (session task)
 * and the report-complete callback (TinyUSB task) send it. One mutex covers
 * both, so a relative-mouse chunk is never sent twice.
 *
 * The wanted state is the host's, and only the host changes it (the session
 * releases everything if the host goes quiet). A target that resets the bus,
 * as one does several times while rebooting, gets the held keys again as
 * soon as it enumerates the device, so "hold F2 and reboot" works.
 */

typedef struct {
    uint8_t modifiers;
    uint8_t keys[6];
} kbd_state_t;

typedef struct {
    uint8_t buttons;
    int32_t dx, dy; /* pending movement */
    int16_t wheel, pan;
} rel_state_t;

typedef struct {
    uint8_t buttons;
    uint16_t x, y;
    int16_t wheel, pan; /* pending detents */
} abs_state_t;

static SemaphoreHandle_t s_lock;

static kbd_state_t s_kbd, s_kbd_sent;
static rel_state_t s_rel;
static uint8_t s_rel_sent_buttons;
static abs_state_t s_abs;
static uint8_t s_abs_sent_buttons;
static uint16_t s_abs_sent_x, s_abs_sent_y;

/* The absolute pointer is the one interface a target may ignore while still
 * using the others (some firmware tools read only the mouse). A report it
 * never collects stays in flight; past ABS_UNREAD_MS that sets
 * TS_ABS_UNREAD, and the next collected report clears it. */
#define ABS_UNREAD_MS 1000 /* long enough for an OS to load its driver for it */
static volatile bool s_abs_in_flight;
static volatile uint32_t s_abs_sent_ms;

static int16_t s_vbus_gpio = SETTING_NONE;

static volatile uint8_t s_kbd_leds;
static volatile bool s_remote_wakeup_enabled;
static volatile bool s_wake_sent;

static uint32_t now_ms(void)
{
    return (uint32_t)(esp_timer_get_time() / 1000);
}

static int8_t clamp8(int32_t v)
{
    return (int8_t)(v > 127 ? 127 : v < -127 ? -127 : v);
}

/* Movement and wheel detents queued for a target that can't take them are stale. */
static void drop_motion_locked(void)
{
    s_rel.dx = s_rel.dy = 0;
    s_rel.wheel = s_rel.pan = 0;
    s_abs.wheel = s_abs.pan = 0;
}

static void clear_input_locked(void)
{
    memset(&s_kbd, 0, sizeof s_kbd);
    memset(&s_rel, 0, sizeof s_rel);
    s_abs.buttons = 0;
    s_abs.wheel = s_abs.pan = 0;
}

/* Each pump_* sends at most one report for its interface. Called with s_lock held. */

static void pump_kbd(void)
{
    if (memcmp(&s_kbd, &s_kbd_sent, sizeof s_kbd) == 0 || !tud_hid_n_ready(ITF_KBD))
        return;
    kbd_state_t k = s_kbd;
    if (tud_hid_n_keyboard_report(ITF_KBD, 0, k.modifiers, k.keys))
        s_kbd_sent = k;
}

static void pump_rel(void)
{
    if (!s_rel.dx && !s_rel.dy && !s_rel.wheel && !s_rel.pan && s_rel.buttons == s_rel_sent_buttons)
        return;
    if (!tud_hid_n_ready(ITF_MOUSE))
        return;
    int8_t x = clamp8(s_rel.dx), y = clamp8(s_rel.dy);
    int8_t wheel = clamp8(s_rel.wheel), pan = clamp8(s_rel.pan);
    uint8_t buttons = s_rel.buttons;
    bool ok;
    if (tud_hid_n_get_protocol(ITF_MOUSE) == HID_PROTOCOL_BOOT) {
        /* Boot protocol: exactly buttons, x, y. Some BIOSes mind extra bytes. */
        const uint8_t r[3] = { buttons, (uint8_t)x, (uint8_t)y };
        ok = tud_hid_n_report(ITF_MOUSE, 0, r, sizeof r);
        wheel = pan = 0; /* the boot report can't carry them; drop rather than queue */
        s_rel.wheel = s_rel.pan = 0;
    } else {
        ok = tud_hid_n_mouse_report(ITF_MOUSE, 0, buttons, x, y, wheel, pan);
    }
    if (ok) {
        s_rel.dx -= x;
        s_rel.dy -= y;
        s_rel.wheel -= wheel;
        s_rel.pan -= pan;
        s_rel_sent_buttons = buttons;
    }
}

static void pump_abs(void)
{
    if (s_abs.x == s_abs_sent_x && s_abs.y == s_abs_sent_y && s_abs.buttons == s_abs_sent_buttons &&
        !s_abs.wheel && !s_abs.pan)
        return;
    if (!tud_hid_n_ready(ITF_ABS))
        return;
    int8_t wheel = clamp8(s_abs.wheel), pan = clamp8(s_abs.pan);
    abs_state_t a = s_abs;
    /* Mark it in flight before queueing it: the completion callback (another
     * task) can run before tud_hid_n_abs_mouse_report returns. */
    s_abs_sent_ms = now_ms();
    s_abs_in_flight = true;
    if (!tud_hid_n_abs_mouse_report(ITF_ABS, 0, a.buttons, (int16_t)a.x, (int16_t)a.y, wheel, pan)) {
        s_abs_in_flight = false;
    } else {
        s_abs.wheel -= wheel;
        s_abs.pan -= pan;
        s_abs_sent_x = a.x;
        s_abs_sent_y = a.y;
        s_abs_sent_buttons = a.buttons;
    }
}

static void pump_all_locked(void)
{
    if (!tud_ready()) /* not mounted, or suspended */
        return;
    pump_kbd();
    pump_rel();
    pump_abs();
}

/* ---- Public API -------------------------------------------------------- */

void usb_hid_keyboard(uint8_t modifiers, const uint8_t keys[6])
{
    xSemaphoreTake(s_lock, portMAX_DELAY);
    s_kbd.modifiers = modifiers;
    memcpy(s_kbd.keys, keys, 6);
    pump_all_locked();
    xSemaphoreGive(s_lock);
}

void usb_hid_mouse_rel(uint8_t buttons, int16_t dx, int16_t dy, int8_t wheel, int8_t pan)
{
    xSemaphoreTake(s_lock, portMAX_DELAY);
    s_rel.buttons = buttons;
    if (tud_ready()) { /* movement only means something to a target that's listening */
        s_rel.dx += dx;
        s_rel.dy += dy;
        s_rel.wheel += wheel;
        s_rel.pan += pan;
    }
    s_abs.buttons = 0; /* the host switched mouse modes; don't leave a button held on the other one */
    pump_all_locked();
    xSemaphoreGive(s_lock);
}

void usb_hid_mouse_abs(uint16_t x, uint16_t y, uint8_t buttons, int8_t wheel, int8_t pan)
{
    xSemaphoreTake(s_lock, portMAX_DELAY);
    s_abs.x = x > 32767 ? 32767 : x;
    s_abs.y = y > 32767 ? 32767 : y;
    s_abs.buttons = buttons;
    if (tud_ready()) {
        s_abs.wheel += wheel;
        s_abs.pan += pan;
    }
    s_rel.buttons = 0;
    pump_all_locked();
    xSemaphoreGive(s_lock);
}

bool usb_hid_input_held(void)
{
    xSemaphoreTake(s_lock, portMAX_DELAY);
    static const uint8_t none[6] = { 0 };
    bool held = s_kbd.modifiers || memcmp(s_kbd.keys, none, sizeof none) != 0 || s_rel.buttons ||
                s_abs.buttons;
    xSemaphoreGive(s_lock);
    return held;
}

void usb_hid_release_all(void)
{
    xSemaphoreTake(s_lock, portMAX_DELAY);
    clear_input_locked();
    pump_all_locked();
    xSemaphoreGive(s_lock);
}

void usb_hid_poll(void)
{
    xSemaphoreTake(s_lock, portMAX_DELAY);
    pump_all_locked();
    xSemaphoreGive(s_lock);
}

/* VBUS sense (optional): the native port's 5 V through a 2:3 divider, on a
 * pin from settings (settings.c checks it). */
bool usb_hid_has_vbus_sense(void)
{
    return s_vbus_gpio >= 0;
}

bool usb_hid_set_vbus_gpio(int16_t pin)
{
    if (pin >= 0) {
        /* No internal pulls: the divider's lower resistor holds the pin low
         * when the target supplies no power, and a pull would shift its
         * levels. */
        const gpio_config_t io = {
            .pin_bit_mask = 1ULL << pin,
            .mode = GPIO_MODE_INPUT,
            .pull_up_en = GPIO_PULLUP_DISABLE,
            .pull_down_en = GPIO_PULLDOWN_DISABLE,
            .intr_type = GPIO_INTR_DISABLE,
        };
        if (gpio_config(&io) != ESP_OK)
            return false;
    }
    if (s_vbus_gpio >= 0 && s_vbus_gpio != pin)
        gpio_reset_pin(s_vbus_gpio);
    s_vbus_gpio = pin;
    return true;
}

uint8_t usb_hid_status(void)
{
    uint8_t f = 0;
    if (tud_mounted()) {
        f |= TS_MOUNTED;
        if (tud_hid_n_get_protocol(ITF_KBD) == HID_PROTOCOL_BOOT)
            f |= TS_KBD_BOOT;
        if (tud_hid_n_get_protocol(ITF_MOUSE) == HID_PROTOCOL_BOOT)
            f |= TS_MOUSE_BOOT;
        if (!tud_suspended() && s_abs_in_flight && now_ms() - s_abs_sent_ms > ABS_UNREAD_MS)
            f |= TS_ABS_UNREAD;
    }
    if (tud_suspended()) {
        f |= TS_SUSPENDED;
        if (s_remote_wakeup_enabled)
            f |= TS_REMOTE_WAKEUP;
    }
    if (s_vbus_gpio >= 0 && gpio_get_level(s_vbus_gpio))
        f |= TS_VBUS_PRESENT;
    return f;
}

uint8_t usb_hid_kbd_leds(void)
{
    return s_kbd_leds;
}

void usb_hid_wake(void)
{
    if (!tud_suspended() || !s_remote_wakeup_enabled || s_wake_sent)
        return;
    s_wake_sent = true;
    if (tinyusb_remote_wakeup() != ESP_OK)
        ESP_LOGW(TAG, "Remote wakeup failed");
}

/* ---- TinyUSB callbacks (TinyUSB task) ---------------------------------- */

/* The target has forgotten everything it was sent. Keep the host's state,
 * but mark nothing as delivered, so whatever is still held is sent again
 * once the target has the device configured. */
static void forget_delivered(void)
{
    xSemaphoreTake(s_lock, portMAX_DELAY);
    drop_motion_locked();
    memset(&s_kbd_sent, 0, sizeof s_kbd_sent);
    s_rel_sent_buttons = s_abs_sent_buttons = 0;
    xSemaphoreGive(s_lock);
    s_abs_in_flight = false; /* the reset dropped it */
}

static void on_usb_event(tinyusb_event_t *event, void *arg)
{
    (void)arg;
    switch (event->id) {
    case TINYUSB_EVENT_ATTACHED:
        /* Each enumeration ends here, including the one after a bus reset,
         * which TinyUSB doesn't report as a detach (e.g., an OS taking over
         * from firmware setup screens). An absolute report in flight then
         * was dropped and will never complete, which would leave
         * TS_ABS_UNREAD set for good, so start afresh. */
        forget_delivered();
        break;
    case TINYUSB_EVENT_DETACHED:
        forget_delivered();
        s_kbd_leds = 0;
        break;
    case TINYUSB_EVENT_SUSPENDED:
        s_remote_wakeup_enabled = event->suspended.remote_wakeup;
        s_wake_sent = false;
        /* Nothing can be sent while suspended. The target keeps what it last
         * saw, and on resume gets any change the host made meanwhile. */
        xSemaphoreTake(s_lock, portMAX_DELAY);
        drop_motion_locked();
        xSemaphoreGive(s_lock);
        break;
    case TINYUSB_EVENT_RESUMED:
        s_remote_wakeup_enabled = false;
        s_abs_sent_ms = now_ms(); /* time asleep doesn't count as ignored */
        break;
    default:
        break;
    }
}

uint8_t const *tud_hid_descriptor_report_cb(uint8_t instance)
{
    switch (instance) {
    case ITF_KBD: return s_report_kbd;
    case ITF_MOUSE: return s_report_mouse;
    default: return s_report_abs;
    }
}

uint16_t tud_hid_get_report_cb(uint8_t instance, uint8_t report_id, hid_report_type_t report_type,
                               uint8_t *buffer, uint16_t reqlen)
{
    (void)instance; (void)report_id; (void)report_type; (void)buffer; (void)reqlen;
    return 0; /* stall: we have nothing to report on request */
}

void tud_hid_set_report_cb(uint8_t instance, uint8_t report_id, hid_report_type_t report_type,
                           uint8_t const *buffer, uint16_t bufsize)
{
    (void)report_id;
    if (instance == ITF_KBD && report_type == HID_REPORT_TYPE_OUTPUT && bufsize >= 1)
        s_kbd_leds = buffer[0] & (KEYBOARD_LED_NUMLOCK | KEYBOARD_LED_CAPSLOCK | KEYBOARD_LED_SCROLLLOCK);
}

void tud_hid_report_complete_cb(uint8_t instance, uint8_t const *report, uint16_t len)
{
    (void)report; (void)len;
    if (instance == ITF_ABS)
        s_abs_in_flight = false; /* the target is reading the absolute pointer */
    /* Send whatever is still pending (the rest of a long relative move, or
     * a state change that arrived while this report was in flight). */
    if (xSemaphoreTake(s_lock, 0) == pdTRUE) { /* if busy, the session task is pumping anyway */
        pump_all_locked();
        xSemaphoreGive(s_lock);
    }
}

/* ---- Init -------------------------------------------------------------- */

void usb_hid_init(void)
{
    s_lock = xSemaphoreCreateMutex();
    configASSERT(s_lock);
    int16_t vbus = settings_get(SETTING_VBUS_GPIO);
    if (vbus >= 0 && !usb_hid_set_vbus_gpio(vbus))
        ESP_LOGE(TAG, "Couldn't set up VBUS sense on GPIO%d", vbus);

    uint8_t mac[6] = { 0 };
    esp_efuse_mac_get_default(mac);
    snprintf(s_serial, sizeof s_serial, "%02X%02X%02X%02X%02X%02X", mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]);

    tinyusb_config_t cfg = TINYUSB_DEFAULT_CONFIG(on_usb_event);
    cfg.descriptor.device = &s_device_desc;
    cfg.descriptor.string = s_strings;
    cfg.descriptor.string_count = STR_COUNT;
    cfg.descriptor.full_speed_config = s_config_desc;
    ESP_ERROR_CHECK(tinyusb_driver_install(&cfg));
}
