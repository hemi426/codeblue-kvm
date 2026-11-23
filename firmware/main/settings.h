/*
 * Board settings saved in flash, so one firmware image suits many boards:
 * which GPIO drives the status LED, which (if any) senses the target's USB
 * power (docs/hardware/vbus-sense.md), and how keep awake behaves
 * (keepawake.h). Each falls back to its default until the viewer sets it.
 *
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#pragma once

#include <stdbool.h>
#include <stdint.h>

/* Setting keys (docs/development/protocol.md, SETTING_GET/SETTING_SET). */
enum {
    SETTING_LED_GPIO = 1,        /* a GPIO, or SETTING_NONE */
    SETTING_VBUS_GPIO = 2,       /* a GPIO, or SETTING_NONE */
    SETTING_KA_MODE = 3,         /* a KA_MODE_* (keepawake.h) */
    SETTING_KA_IDLE_DELAY = 4,   /* seconds without input before acting */
    SETTING_KA_INTERVAL_MIN = 5, /* seconds between actions: at least... */
    SETTING_KA_INTERVAL_MAX = 6, /* ...and at most (each wait is random within) */
};

/* For any setting, SETTING_DEFAULT (settings_set only) forgets the saved
 * value and goes back to the default. */
#define SETTING_NONE    (-1)
#define SETTING_DEFAULT (-2)

/* Results of settings_set. */
typedef enum {
    SETTING_OK = 0,
    SETTING_ERR_UNKNOWN = 1,  /* no such setting */
    SETTING_ERR_PIN = 2,      /* not a GPIO this board can use for that */
    SETTING_ERR_CONFLICT = 3, /* clashes with another setting: a pin in use, or
                               * the interval's ends out of order */
    SETTING_ERR_APPLY = 4,    /* couldn't apply or save it */
    SETTING_ERR_RANGE = 5,    /* outside the setting's range */
} setting_result_t;

/* Loads the saved settings. Needs NVS initialized; call before the LED and
 * USB are set up. */
void settings_init(void);

/* True for a key this firmware has. */
bool settings_known(uint8_t key);

/* The current value of a setting, or SETTING_NONE for an unknown key. */
int16_t settings_get(uint8_t key);

/* Checks, applies, and saves a new value. A value that fails the checks
 * changes nothing; if only saving fails (SETTING_ERR_APPLY after applying),
 * the new value lasts until the board restarts. */
setting_result_t settings_set(uint8_t key, int16_t value);
