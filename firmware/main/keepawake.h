/*
 * Keep awake: while the target sees no real input, the board makes a small,
 * harmless one now and then, so the target doesn't sleep or lock. The viewer
 * turns it on and off (KEEP_AWAKE); how and when it acts are board settings
 * (settings.h). It's always off after the board loses power.
 *
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#pragma once

#include <stdbool.h>
#include <stdint.h>

/* Modes (the SETTING_KA_MODE values). */
enum {
    KA_MODE_HIDDEN_KEY = 0, /* taps F15, which no common keyboard has */
    KA_MODE_NUDGE = 1,      /* one count right, then back */
    KA_MODE_WANDER = 2,     /* a short glide at a random angle, then back */
    KA_MODE_SCROLL = 3,     /* one wheel detent down, then back up */
    KA_MODE_MIXED = 4,      /* one of the others at random, each time */
    KA_MODE_COUNT
};

/* States for KEEP_AWAKE_STATUS. */
enum {
    KA_OFF = 0,
    KA_WAITING = 1, /* on, counting down the idle delay */
    KA_ACTIVE = 2,  /* on, acting every interval */
    KA_PAUSED = 3,  /* on, but the target is asleep or not connected */
};

void keepawake_set_enabled(bool on);
bool keepawake_enabled(void);

/* Real input reached the target: start the idle delay again. */
void keepawake_input(void);

/* The settings changed: start the idle delay again with the new values. */
void keepawake_settings_changed(void);

/* Runs keep awake. Call often (every few milliseconds), from one task. */
void keepawake_poll(void);

/* The current state, and for KA_WAITING the seconds until it starts acting. */
uint8_t keepawake_state(uint16_t *seconds);

/* True once if the state (or the countdown's end) changed since the last
 * call: time for the viewer to hear about it. */
bool keepawake_report_due(void);
