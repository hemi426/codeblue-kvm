/*
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#include "settings.h"

#include <stdbool.h>

#include "esp_log.h"
#include "nvs.h"

#include "status_led.h"
#include "usb_hid.h"

static const char *TAG = "settings";

#define NVS_NAMESPACE "codeblue"
#define NVS_KEY_LED_GPIO "led_gpio"
#define NVS_KEY_VBUS_GPIO "vbus_gpio"

static int16_t s_led_gpio = CONFIG_CODEBLUE_LED_GPIO;
static int16_t s_vbus_gpio = CONFIG_CODEBLUE_VBUS_GPIO;

/* GPIOs the ESP32-S3 can't spare on a DevKitC-style board: the strapping
 * pins (0, 3, 45, 46), the native USB port (19, 20), the SPI flash (26 to
 * 32), octal flash or PSRAM on many modules (33 to 37), and the UART that
 * carries the host protocol (43, 44). 22 to 25 don't exist. */
static bool pin_reserved(int16_t pin)
{
    switch (pin) {
    case 0: case 3: case 45: case 46:
    case 19: case 20:
    case 43: case 44:
        return true;
    default:
        return (pin >= 22 && pin <= 37) || pin < 0 || pin > 48;
    }
}

static bool pin_ok(uint8_t key, int16_t pin)
{
    if (pin == SETTING_NONE)
        return true;
    if (pin_reserved(pin))
        return false;
    /* 47 and 48 run at 1.8 V on modules with 1.8 V flash: fine for driving
     * an LED, not for a 3 V sense divider. */
    if (key == SETTING_VBUS_GPIO && (pin == 47 || pin == 48))
        return false;
    return true;
}

static int16_t build_default(uint8_t key)
{
    return key == SETTING_LED_GPIO ? CONFIG_CODEBLUE_LED_GPIO : CONFIG_CODEBLUE_VBUS_GPIO;
}

static const char *nvs_key(uint8_t key)
{
    return key == SETTING_LED_GPIO ? NVS_KEY_LED_GPIO : NVS_KEY_VBUS_GPIO;
}

static void load(nvs_handle_t nvs, uint8_t key, int16_t *value)
{
    int16_t v;
    if (nvs_get_i16(nvs, nvs_key(key), &v) == ESP_OK && pin_ok(key, v))
        *value = v;
}

void settings_init(void)
{
    nvs_handle_t nvs;
    if (nvs_open(NVS_NAMESPACE, NVS_READONLY, &nvs) == ESP_OK) {
        load(nvs, SETTING_LED_GPIO, &s_led_gpio);
        load(nvs, SETTING_VBUS_GPIO, &s_vbus_gpio);
        nvs_close(nvs);
    }
    /* A saved pin that clashes with the other one (say, a saved VBUS pin
     * that a later LED default lands on) loses to the LED. */
    if (s_vbus_gpio >= 0 && s_vbus_gpio == s_led_gpio) {
        ESP_LOGW(TAG, "VBUS sense pin GPIO%d is the LED's; turning VBUS sense off", s_vbus_gpio);
        s_vbus_gpio = SETTING_NONE;
    }
}

int16_t settings_get(uint8_t key)
{
    switch (key) {
    case SETTING_LED_GPIO: return s_led_gpio;
    case SETTING_VBUS_GPIO: return s_vbus_gpio;
    default: return SETTING_NONE;
    }
}

static bool save(uint8_t key, int16_t value, bool forget)
{
    nvs_handle_t nvs;
    if (nvs_open(NVS_NAMESPACE, NVS_READWRITE, &nvs) != ESP_OK)
        return false;
    esp_err_t err = forget ? nvs_erase_key(nvs, nvs_key(key)) : nvs_set_i16(nvs, nvs_key(key), value);
    if (err == ESP_ERR_NVS_NOT_FOUND)
        err = ESP_OK; /* forgetting a value that was never saved */
    if (err == ESP_OK)
        err = nvs_commit(nvs);
    nvs_close(nvs);
    return err == ESP_OK;
}

setting_result_t settings_set(uint8_t key, int16_t value)
{
    if (key != SETTING_LED_GPIO && key != SETTING_VBUS_GPIO)
        return SETTING_ERR_UNKNOWN;

    bool forget = (value == SETTING_DEFAULT);
    if (forget)
        value = build_default(key);
    if (!pin_ok(key, value))
        return SETTING_ERR_PIN;
    int16_t other = settings_get(key == SETTING_LED_GPIO ? SETTING_VBUS_GPIO : SETTING_LED_GPIO);
    if (value >= 0 && value == other)
        return SETTING_ERR_CONFLICT;

    if (value != settings_get(key)) {
        bool applied = key == SETTING_LED_GPIO ? status_led_set_gpio(value) : usb_hid_set_vbus_gpio(value);
        if (!applied)
            return SETTING_ERR_APPLY;
        if (key == SETTING_LED_GPIO)
            s_led_gpio = value;
        else
            s_vbus_gpio = value;
    }
    return save(key, value, forget) ? SETTING_OK : SETTING_ERR_APPLY;
}
