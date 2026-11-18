/*
 * The board's addressable RGB status LED, with a brightness that scales
 * every colour and can be saved in flash.
 *
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#pragma once

#include <stdbool.h>
#include <stdint.h>

/* Status colours (full scale; brightness is applied on top). */
#define LED_NO_HOST 0, 0, 255 /* blue: running, waiting for the viewer */
#define LED_HOST    0, 255, 0 /* green: viewer connected */

/* Initializes the LED on the pin from settings (settings_init first) and
 * loads the saved brightness. Needs NVS initialized. */
void status_led_init(void);

/* Moves the LED to another GPIO (or none, for a negative pin) and shows the
 * current colour there. False if the LED driver couldn't be set up on it. */
bool status_led_set_gpio(int16_t pin);

void status_led_set(uint8_t r, uint8_t g, uint8_t b);

uint8_t status_led_brightness(void);

/* Sets the brightness (0-100) and re-shows the current colour. With `persist`,
 * also saves it in flash if it differs from the stored value. */
void status_led_set_brightness(uint8_t percent, bool persist);
