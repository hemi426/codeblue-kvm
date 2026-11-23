/*
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#include "keepawake.h"

#include <math.h>
#include <string.h>

#include "esp_random.h"
#include "esp_timer.h"

#include "settings.h"
#include "status_led.h"
#include "usb_hid.h"

#define HID_KEY_F15 0x6A

/* A step is one small change to the target's input; an action is a few
 * steps a short way apart (press, release; out, back). */
#define MAX_STEPS 40

typedef struct {
    uint8_t key;       /* key held after this step (0: none) */
    int8_t dx, dy;     /* relative movement in this step */
    int8_t wheel;
    uint16_t delay_ms; /* before the next step */
} step_t;

static bool s_enabled;
static int64_t s_idle_since_ms;  /* last real input, or when it was turned on */
static int64_t s_next_action_ms; /* once active */
static bool s_active;            /* the idle delay has passed */

static step_t s_steps[MAX_STEPS];
static int s_step_count, s_step_next;
static int64_t s_step_due_ms;

/* So nobody forgets it's on: a white heartbeat on the status LED ("lub-dub":
 * two quick flashes), at a resting rate while waiting or paused and faster
 * while acting. */
#define BEAT_RESTING_MS 3000
#define BEAT_ACTIVE_MS 1500
#define BEAT_FLASH_MS 100
#define BEAT_GAP_MS 120
static int64_t s_flash_start_ms;

static uint8_t s_reported_state = 0xFF;
static int64_t s_reported_end_ms;

static int64_t now_ms(void)
{
    return esp_timer_get_time() / 1000;
}

static uint32_t random_between(uint32_t lo, uint32_t hi)
{
    return hi <= lo ? lo : lo + esp_random() % (hi - lo + 1);
}

static int64_t idle_delay_ms(void)
{
    return (int64_t)settings_get(SETTING_KA_IDLE_DELAY) * 1000;
}

static int64_t random_interval_ms(void)
{
    uint32_t lo = (uint32_t)settings_get(SETTING_KA_INTERVAL_MIN) * 1000;
    uint32_t hi = (uint32_t)settings_get(SETTING_KA_INTERVAL_MAX) * 1000;
    return random_between(lo, hi);
}

static void add_step(uint8_t key, int dx, int dy, int wheel, uint16_t delay_ms)
{
    if (s_step_count < MAX_STEPS)
        s_steps[s_step_count++] = (step_t){ key, (int8_t)dx, (int8_t)dy, (int8_t)wheel, delay_ms };
}

/* A glide of a few dozen counts at a random angle, in uneven steps with a
 * little sideways drift, then back along roughly the same path. */
static void plan_wander(void)
{
    float angle = (float)(esp_random() % 3600) * (float)M_PI / 1800.0f;
    int length = (int)random_between(20, 60);
    int steps = (int)random_between(8, 14);
    float ux = cosf(angle), uy = sinf(angle);
    int moved_x = 0, moved_y = 0;
    for (int i = 1; i <= steps; i++) {
        float target = (float)length * (float)i / (float)steps;
        float drift = (float)((int)random_between(0, 4) - 2) * 0.5f;
        int to_x = (int)lroundf(target * ux - drift * uy);
        int to_y = (int)lroundf(target * uy + drift * ux);
        add_step(0, to_x - moved_x, to_y - moved_y, 0, (uint16_t)random_between(12, 40));
        moved_x = to_x;
        moved_y = to_y;
    }
    /* A pause, then home in fewer, quicker steps. */
    s_steps[s_step_count - 1].delay_ms = (uint16_t)random_between(150, 600);
    int back = (int)random_between(4, 8);
    int left_x = moved_x, left_y = moved_y;
    for (int i = back; i >= 1; i--) {
        int to_x = moved_x * (i - 1) / back, to_y = moved_y * (i - 1) / back;
        add_step(0, to_x - left_x, to_y - left_y, 0, (uint16_t)random_between(10, 30));
        left_x = to_x;
        left_y = to_y;
    }
}

static void plan_action(void)
{
    s_step_count = 0;
    s_step_next = 0;
    int mode = settings_get(SETTING_KA_MODE);
    if (mode == KA_MODE_MIXED)
        mode = (int)random_between(0, KA_MODE_MIXED - 1);
    switch (mode) {
    case KA_MODE_HIDDEN_KEY:
        add_step(HID_KEY_F15, 0, 0, 0, (uint16_t)random_between(40, 90));
        add_step(0, 0, 0, 0, 0);
        break;
    case KA_MODE_NUDGE:
        add_step(0, 1, 0, 0, (uint16_t)random_between(40, 120));
        add_step(0, -1, 0, 0, 0);
        break;
    case KA_MODE_SCROLL:
        add_step(0, 0, 0, -1, (uint16_t)random_between(150, 400));
        add_step(0, 0, 0, 1, 0);
        break;
    default:
        plan_wander();
        break;
    }
    s_step_due_ms = now_ms();
}

/* Sends the next step of the action under way, if it's time. */
static void run_steps(int64_t now)
{
    if (s_step_next >= s_step_count || now < s_step_due_ms)
        return;
    const step_t *s = &s_steps[s_step_next++];
    if (s->key || (s_step_next > 1 && s_steps[s_step_next - 2].key)) {
        const uint8_t keys[6] = { s->key };
        usb_hid_keyboard(0, keys);
    }
    if (s->dx || s->dy || s->wheel)
        usb_hid_mouse_rel(0, s->dx, s->dy, s->wheel, 0);
    s_step_due_ms = now + s->delay_ms;
}

static bool target_listening(void)
{
    uint8_t f = usb_hid_status();
    return (f & TS_MOUNTED) && !(f & TS_SUSPENDED);
}

static void restart_idle(void)
{
    s_idle_since_ms = now_ms();
    s_active = false;
}

void keepawake_set_enabled(bool on)
{
    if (on && !s_enabled)
        restart_idle();
    s_enabled = on;
}

bool keepawake_enabled(void)
{
    return s_enabled;
}

void keepawake_input(void)
{
    restart_idle();
}

void keepawake_settings_changed(void)
{
    restart_idle();
}

/* One heartbeat at the start of each period: flash, gap, flash. */
static void update_flash(int64_t now)
{
    if (!s_enabled) {
        status_led_flash(false);
        return;
    }
    int64_t period = s_active ? BEAT_ACTIVE_MS : BEAT_RESTING_MS;
    int64_t t = now - s_flash_start_ms;
    if (t >= period) {
        s_flash_start_ms = now;
        t = 0;
    }
    const int64_t dub = BEAT_FLASH_MS + BEAT_GAP_MS;
    bool on = t < BEAT_FLASH_MS || (t >= dub && t < dub + BEAT_FLASH_MS);
    status_led_flash(on);
}

void keepawake_poll(void)
{
    int64_t now = now_ms();
    update_flash(now);

    /* An action under way finishes even if keep awake was just turned off
     * or input arrived, so nothing is left held or out of place. */
    if (s_step_next < s_step_count) {
        if (target_listening())
            run_steps(now);
        else
            s_step_count = s_step_next = 0; /* the target can't take it; nothing was left held */
        return;
    }

    if (!s_enabled)
        return;
    if (!target_listening()) {
        /* Asleep or not connected: never wake it, and don't count the time. */
        restart_idle();
        return;
    }
    if (!s_active) {
        if (now - s_idle_since_ms < idle_delay_ms())
            return;
        s_active = true;
        s_next_action_ms = now; /* act straight away, then every interval */
    }
    if (now < s_next_action_ms)
        return;
    s_next_action_ms = now + random_interval_ms();
    if (usb_hid_input_held())
        return; /* someone's holding a key or button: leave the target alone */
    plan_action();
    run_steps(now);
}

uint8_t keepawake_state(uint16_t *seconds)
{
    *seconds = 0;
    if (!s_enabled)
        return KA_OFF;
    if (!target_listening())
        return KA_PAUSED;
    if (s_active)
        return KA_ACTIVE;
    int64_t left = s_idle_since_ms + idle_delay_ms() - now_ms();
    *seconds = left <= 0 ? 0 : (uint16_t)((left + 999) / 1000);
    return KA_WAITING;
}

bool keepawake_report_due(void)
{
    uint16_t seconds;
    uint8_t state = keepawake_state(&seconds);
    int64_t end = state == KA_WAITING ? s_idle_since_ms + idle_delay_ms() : 0;
    /* While waiting, input keeps moving the end of the countdown; only report
     * it when it has moved by a second or more, so typing doesn't flood the link. */
    int64_t moved = end - s_reported_end_ms;
    if (state == s_reported_state && (moved < 1000 && moved > -1000))
        return false;
    s_reported_state = state;
    s_reported_end_ms = end;
    return true;
}
