# CodeBlue firmware (ESP-IDF). Build from this folder after activating ESP-IDF:
#   . ../scripts/activate-idf.ps1   (or .sh)
#   idf.py build
cmake_minimum_required(VERSION 3.16)

set(PROJECT_VER "0.1.0")
include($ENV{IDF_PATH}/tools/cmake/project.cmake)
project(codeblue)
