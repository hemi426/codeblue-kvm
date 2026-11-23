/*
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#include "status_led.h"

#include "esp_log.h"
#include "led_strip.h"
#include "nvs.h"

#include "settings.h"

static const char *TAG = "led";

#define NVS_NAMESPACE "codeblue"
#define NVS_KEY_BRIGHTNESS "led_pct"

static led_strip_handle_t s_led;
static uint8_t s_r, s_g, s_b;
static bool s_flash;
static uint8_t s_brightness = CONFIG_CODEBLUE_LED_BRIGHTNESS;

static uint8_t scale(uint8_t v)
{
    return (uint8_t)((v * s_brightness + 50) / 100);
}

static void show(void)
{
    if (!s_led)
        return;
    static const uint8_t flash[3] = { LED_FLASH };
    if (s_flash)
        led_strip_set_pixel(s_led, 0, scale(flash[0]), scale(flash[1]), scale(flash[2]));
    else
        led_strip_set_pixel(s_led, 0, scale(s_r), scale(s_g), scale(s_b));
    led_strip_refresh(s_led);
}

/* Creates an LED driver on `pin` in *out, or none (NULL) for a negative pin. */
static bool attach(int16_t pin, led_strip_handle_t *out)
{
    *out = NULL;
    if (pin < 0)
        return true;
    const led_strip_config_t strip_config = {
        .strip_gpio_num = pin,
        .max_leds = 1,
        .led_model = LED_MODEL_WS2812,
        .color_component_format = LED_STRIP_COLOR_COMPONENT_FMT_GRB,
    };
    const led_strip_rmt_config_t rmt_config = {
        .clk_src = RMT_CLK_SRC_DEFAULT,
        .resolution_hz = 10 * 1000 * 1000, /* 10 MHz: 0.1 us per tick */
    };
    if (led_strip_new_rmt_device(&strip_config, &rmt_config, out) != ESP_OK) {
        *out = NULL;
        return false;
    }
    return true;
}

void status_led_init(void)
{
    nvs_handle_t nvs;
    if (nvs_open(NVS_NAMESPACE, NVS_READONLY, &nvs) == ESP_OK) {
        uint8_t v;
        if (nvs_get_u8(nvs, NVS_KEY_BRIGHTNESS, &v) == ESP_OK && v <= 100)
            s_brightness = v;
        nvs_close(nvs);
    }

    int16_t pin = settings_get(SETTING_LED_GPIO);
    if (!attach(pin, &s_led))
        ESP_LOGE(TAG, "No LED on GPIO%d; carrying on without it", pin);
}

bool status_led_set_gpio(int16_t pin)
{
    /* Set up the new pin first, so a failure leaves the old LED working. */
    led_strip_handle_t led;
    if (!attach(pin, &led))
        return false;
    if (s_led) {
        led_strip_clear(s_led); /* don't leave the old pin's LED lit */
        led_strip_del(s_led);
    }
    s_led = led;
    show();
    return true;
}

void status_led_set(uint8_t r, uint8_t g, uint8_t b)
{
    s_r = r;
    s_g = g;
    s_b = b;
    show();
}

void status_led_flash(bool on)
{
    if (on == s_flash)
        return;
    s_flash = on;
    show();
}

uint8_t status_led_brightness(void)
{
    return s_brightness;
}

void status_led_set_brightness(uint8_t percent, bool persist)
{
    if (percent > 100)
        percent = 100;
    s_brightness = percent;
    show();

    if (!persist)
        return;
    nvs_handle_t nvs;
    if (nvs_open(NVS_NAMESPACE, NVS_READWRITE, &nvs) != ESP_OK)
        return;
    uint8_t stored;
    if (nvs_get_u8(nvs, NVS_KEY_BRIGHTNESS, &stored) != ESP_OK || stored != percent) {
        nvs_set_u8(nvs, NVS_KEY_BRIGHTNESS, percent);
        nvs_commit(nvs);
    }
    nvs_close(nvs);
}
