/*
 * Board settings saved in flash, so one firmware image suits many boards:
 * which GPIO drives the status LED, and which (if any) senses the target's
 * USB power (docs/hardware/vbus-sense.md). Each falls back to its build
 * default (Kconfig) until the viewer sets it.
 *
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#pragma once

#include <stdint.h>

/* Setting keys (docs/development/protocol.md, SETTING_GET/SETTING_SET). */
enum {
    SETTING_LED_GPIO = 1,
    SETTING_VBUS_GPIO = 2,
};

/* Values: a GPIO number, SETTING_NONE, or (for settings_set only)
 * SETTING_DEFAULT, which forgets the saved value and goes back to the build
 * default. */
#define SETTING_NONE    (-1)
#define SETTING_DEFAULT (-2)

/* Results of settings_set. */
typedef enum {
    SETTING_OK = 0,
    SETTING_ERR_UNKNOWN = 1,  /* no such setting */
    SETTING_ERR_PIN = 2,      /* not a GPIO this board can use for that */
    SETTING_ERR_CONFLICT = 3, /* the other setting already uses that pin */
    SETTING_ERR_APPLY = 4,    /* couldn't apply or save it */
} setting_result_t;

/* Loads the saved settings. Needs NVS initialized; call before the LED and
 * USB are set up. */
void settings_init(void);

/* The current value of a setting, or SETTING_NONE for an unknown key. */
int16_t settings_get(uint8_t key);

/* Checks, applies, and saves a new value. A value that fails the checks
 * changes nothing; if only saving fails (SETTING_ERR_APPLY after applying),
 * the new value lasts until the board restarts. */
setting_result_t settings_set(uint8_t key, int16_t value);
