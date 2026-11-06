/*
 * CodeBlue firmware for the ESP32-S3.
 *
 * Skeleton: proves the toolchain and managed components build, and lights
 * the status LED dim blue. The USB HID device and the serial protocol
 * (docs/PROTOCOL.md) come next.
 *
 * SPDX-License-Identifier: GPL-3.0-or-later
 */

#include <stdint.h>

#include "esp_app_desc.h"
#include "esp_err.h"
#include "esp_log.h"
#include "led_strip.h"
#include "tinyusb.h" /* not used yet; included so the build checks the component */

static const char *TAG = "codeblue";

static led_strip_handle_t s_led;

/* Scales a full-scale colour channel by the configured brightness. */
static uint8_t led_scale(uint8_t v)
{
    return (uint8_t)((v * CONFIG_CODEBLUE_LED_BRIGHTNESS + 50) / 100);
}

static void led_set(uint8_t r, uint8_t g, uint8_t b)
{
    ESP_ERROR_CHECK(led_strip_set_pixel(s_led, 0, led_scale(r), led_scale(g), led_scale(b)));
    ESP_ERROR_CHECK(led_strip_refresh(s_led));
}

static void led_init(void)
{
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
    ESP_ERROR_CHECK(led_strip_new_rmt_device(&strip_config, &rmt_config, &s_led));
}

void app_main(void)
{
    led_init();
    led_set(0, 0, 255); /* blue: running, no host yet */

    ESP_LOGI(TAG, "CodeBlue firmware %s (skeleton). LED on GPIO%d at %d%%.",
             esp_app_get_description()->version,
             CONFIG_CODEBLUE_LED_GPIO, CONFIG_CODEBLUE_LED_BRIGHTNESS);
}
