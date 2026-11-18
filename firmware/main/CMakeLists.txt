idf_component_register(SRCS "main.c" "frame.c" "session.c" "settings.c" "status_led.c" "usb_hid.c"
                       INCLUDE_DIRS "."
                       PRIV_REQUIRES esp_app_format esp_driver_gpio esp_driver_uart esp_hw_support esp_timer nvs_flash)
