/*
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#include "status_led.h"

#include "esp_log.h"
#include "led_strip.h"
#include "nvs.h"

static const char *TAG = "led";

#define NVS_NAMESPACE "codeblue"
#define NVS_KEY_BRIGHTNESS "led_pct"

static led_strip_handle_t s_led;
static uint8_t s_r, s_g, s_b;
static uint8_t s_brightness = CONFIG_CODEBLUE_LED_BRIGHTNESS;

static uint8_t scale(uint8_t v)
{
    return (uint8_t)((v * s_brightness + 50) / 100);
}

static void show(void)
{
    if (!s_led)
        return;
    led_strip_set_pixel(s_led, 0, scale(s_r), scale(s_g), scale(s_b));
    led_strip_refresh(s_led);
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

    const led_strip_config_t strip_config = {
        .strip_gpio_num = CONFIG_CODEBLUE_LED_GPIO,
        .max_leds = 1,
        .led_model = LED_MODEL_WS2812,
        .color_component_format = LED_STRIP_COLOR_COMPONENT_FMT_GRB,
    };
    const led_strip_rmt_config_t rmt_config = {
        .clk_src = RMT_CLK_SRC_DEFAULT,
        .resolution_hz = 10 * 1000 * 1000, /* 10 MHz: 0.1 us per tick */
    };
    if (led_strip_new_rmt_device(&strip_config, &rmt_config, &s_led) != ESP_OK) {
        ESP_LOGE(TAG, "No LED on GPIO%d; carrying on without it", CONFIG_CODEBLUE_LED_GPIO);
        s_led = NULL;
    }
}

void status_led_set(uint8_t r, uint8_t g, uint8_t b)
{
    s_r = r;
    s_g = g;
    s_b = b;
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
