/*
 * The USB device the target sees: one composite device with a boot-protocol
 * keyboard, a boot-protocol relative mouse and an absolute pointer.
 *
 * Callers set the desired state; usb_hid_poll() turns it into HID reports
 * whenever the target is ready for one. A report the target isn't ready for
 * is never lost: the latest state is sent as soon as it is.
 *
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#pragma once

#include <stdbool.h>
#include <stdint.h>

/* TARGET_STATUS flag bits (docs/development/protocol.md). */
#define TS_MOUNTED       (1u << 0)
#define TS_SUSPENDED     (1u << 1)
#define TS_KBD_BOOT      (1u << 2)
#define TS_MOUSE_BOOT    (1u << 3)
#define TS_REMOTE_WAKEUP (1u << 4)
#define TS_VBUS_PRESENT  (1u << 5)
#define TS_ABS_UNREAD    (1u << 6)

void usb_hid_init(void);

/* Complete keyboard state: HID modifier byte and up to six usages (0 = none). */
void usb_hid_keyboard(uint8_t modifiers, const uint8_t keys[6]);

/* Relative movement since the last call; large deltas are split into several reports. */
void usb_hid_mouse_rel(uint8_t buttons, int16_t dx, int16_t dy, int8_t wheel, int8_t pan);

/* Absolute position, 0-32767 on each axis across the target's screen. */
void usb_hid_mouse_abs(uint16_t x, uint16_t y, uint8_t buttons, int8_t wheel, int8_t pan);

/* Releases every key and button. */
void usb_hid_release_all(void);

/* Sends any pending reports the target is ready for. Call often. */
void usb_hid_poll(void);

/* TS_* flags. */
uint8_t usb_hid_status(void);

/* True if a VBUS sense pin is set (settings.h). */
bool usb_hid_has_vbus_sense(void);

/* Senses VBUS on `pin` from now on (none for a negative pin), releasing the
 * old pin. The caller checks the pin is usable. False if it can't be set up. */
bool usb_hid_set_vbus_gpio(int16_t pin);

/* The target's Num/Caps/Scroll Lock LEDs (bits 0-2). */
uint8_t usb_hid_kbd_leds(void);

/* If the target is suspended and allows it, signals remote wakeup (once per suspend). */
void usb_hid_wake(void);
