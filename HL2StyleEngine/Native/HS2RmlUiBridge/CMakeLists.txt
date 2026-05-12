cmake_minimum_required(VERSION 3.24)

project(HS2RmlUiBridge LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

set(HS2_ROOT "${CMAKE_CURRENT_LIST_DIR}/../..")
set(RMLUI_ROOT "${CMAKE_CURRENT_LIST_DIR}/../external/RmlUi")
set(RMLUI_BUILD_ROOT "${HS2_ROOT}/../.codex-build/RmlUi")

add_library(HS2RmlUiBridge SHARED
    HS2RmlUiBridge.cpp
    "${RMLUI_ROOT}/Tests/Dependencies/lodepng/lodepng.cpp")

target_include_directories(HS2RmlUiBridge PRIVATE
    "${CMAKE_CURRENT_LIST_DIR}"
    "${RMLUI_ROOT}/Include"
    "${RMLUI_ROOT}/Tests/Dependencies/lodepng")

target_compile_definitions(HS2RmlUiBridge PRIVATE
    HS2_RMLUI_BRIDGE_BUILD
    RMLUI_STATIC_LIB
    NOMINMAX
    _CRT_SECURE_NO_WARNINGS)

target_link_libraries(HS2RmlUiBridge PRIVATE
    "${RMLUI_BUILD_ROOT}/Release/rmlui.lib")

if (MSVC)
    target_compile_options(HS2RmlUiBridge PRIVATE /W4 /permissive-)
endif()
