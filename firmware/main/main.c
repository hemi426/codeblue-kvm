/*
 * CodeBlue firmware for the ESP32-S3.
 *
 * The native USB port is a keyboard, mouse and absolute pointer for the
 * target (usb_hid.c). The UART port talks to the viewer with the binary
 * protocol in docs/development/protocol.md (session.c, frame.c). The RGB LED
 * shows status, or whatever colour the viewer asks for (status_led.c). Board
 * settings such as the LED's pin are saved in flash (settings.c). Keep awake
 * stops the target sleeping while nobody's using it (keepawake.c).
 *
 * SPDX-License-Identifier: GPL-3.0-or-later
 */

#include "esp_app_desc.h"
#include "esp_err.h"
#include "esp_log.h"
#include "nvs_flash.h"

#include "session.h"
#include "settings.h"
#include "status_led.h"
#include "usb_hid.h"

static const char *TAG = "codeblue";

static void nvs_init(void)
{
    esp_err_t err = nvs_flash_init();
    if (err == ESP_ERR_NVS_NO_FREE_PAGES || err == ESP_ERR_NVS_NEW_VERSION_FOUND) {
        /* Only settings live here (LED brightness, pins); start over rather than fail. */
        ESP_ERROR_CHECK(nvs_flash_erase());
        err = nvs_flash_init();
    }
    ESP_ERROR_CHECK(err);
}

void app_main(void)
{
    nvs_init();
    settings_init();
    status_led_init();
    status_led_set(LED_NO_HOST);

    ESP_LOGI(TAG, "CodeBlue firmware %s on %s. LED on GPIO%d at %d%%; VBUS sense %s.",
             esp_app_get_description()->version, CONFIG_CODEBLUE_BOARD_NAME,
             settings_get(SETTING_LED_GPIO), status_led_brightness(),
             settings_get(SETTING_VBUS_GPIO) >= 0 ? "on" : "off");

    usb_hid_init();
    session_start(); /* last: it turns logging off */
}
