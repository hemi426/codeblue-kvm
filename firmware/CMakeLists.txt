# CodeBlue firmware (ESP-IDF). Build from this folder after activating ESP-IDF:
#   . ../scripts/activate-idf.ps1   (or .sh)
#   idf.py build                    (or: idf.py -DBOARD=devkitc-v1.1 build)
cmake_minimum_required(VERSION 3.16)

set(PROJECT_VER "0.1.1")

# Board preset: boards/<BOARD>.defaults is layered over sdkconfig.defaults.
set(BOARD "devkitc-v1.0" CACHE STRING "Board preset, from boards/")
set(_board_file "${CMAKE_CURRENT_LIST_DIR}/boards/${BOARD}.defaults")
if(NOT EXISTS "${_board_file}")
    file(GLOB _boards RELATIVE "${CMAKE_CURRENT_LIST_DIR}/boards" "${CMAKE_CURRENT_LIST_DIR}/boards/*.defaults")
    string(REPLACE ".defaults" "" _boards "${_boards}")
    message(FATAL_ERROR "Unknown BOARD \"${BOARD}\". Available: ${_boards}")
endif()
# Defaults only apply when sdkconfig is first created, so an sdkconfig made
# for another board (or before a setting existed) would silently win.
file(STRINGS "${_board_file}" _want REGEX "^CONFIG_CODEBLUE_BOARD_NAME=")
if(EXISTS "${CMAKE_CURRENT_LIST_DIR}/sdkconfig")
    file(STRINGS "${CMAKE_CURRENT_LIST_DIR}/sdkconfig" _have REGEX "^CONFIG_CODEBLUE_BOARD_NAME=")
    if(NOT _have STREQUAL _want)
        message(FATAL_ERROR
            "firmware/sdkconfig was made for a different board or an older version of these "
            "settings (found [${_have}], BOARD ${BOARD} wants [${_want}]). "
            "Delete firmware/sdkconfig and build again: idf.py -DBOARD=${BOARD} fullclean build")
    endif()
endif()
set(SDKCONFIG_DEFAULTS "sdkconfig.defaults;boards/${BOARD}.defaults")

include($ENV{IDF_PATH}/tools/cmake/project.cmake)
# Build only what main depends on, not every component in ESP-IDF.
idf_build_set_property(MINIMAL_BUILD ON)
project(codeblue)
