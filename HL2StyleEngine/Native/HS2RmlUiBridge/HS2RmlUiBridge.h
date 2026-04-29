#pragma once

#include <stdint.h>

#if defined(_WIN32)
#  if defined(HS2_RMLUI_BRIDGE_BUILD)
#    define HS2_RMLUI_API __declspec(dllexport)
#  else
#    define HS2_RMLUI_API __declspec(dllimport)
#  endif
#else
#  define HS2_RMLUI_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef void* hs2_rmlui_context;
typedef void* hs2_rmlui_document;

typedef struct hs2_rmlui_vertex
{
    float x;
    float y;
    float u;
    float v;
    // Packed as 0xAABBGGRR so the least-significant byte is red.
    uint32_t color_rgba;
} hs2_rmlui_vertex;

typedef struct hs2_rmlui_render_command
{
    const hs2_rmlui_vertex* vertices;
    int32_t vertex_count;
    const uint32_t* indices;
    int32_t index_count;
    uint64_t texture_id;
    int32_t scissor_enabled;
    int32_t scissor_x;
    int32_t scissor_y;
    int32_t scissor_width;
    int32_t scissor_height;
    float translate_x;
    float translate_y;
} hs2_rmlui_render_command;

typedef struct hs2_rmlui_render_data
{
    const hs2_rmlui_render_command* commands;
    int32_t command_count;
    int32_t viewport_width;
    int32_t viewport_height;
} hs2_rmlui_render_data;

HS2_RMLUI_API int32_t hs2_rmlui_create_context(
    const char* content_root_utf8,
    int32_t width,
    int32_t height,
    hs2_rmlui_context* out_context);

HS2_RMLUI_API void hs2_rmlui_destroy_context(hs2_rmlui_context context);

HS2_RMLUI_API void hs2_rmlui_set_viewport(
    hs2_rmlui_context context,
    int32_t width,
    int32_t height);

HS2_RMLUI_API int32_t hs2_rmlui_load_document(
    hs2_rmlui_context context,
    const char* document_path_utf8,
    hs2_rmlui_document* out_document);

HS2_RMLUI_API void hs2_rmlui_show_document(hs2_rmlui_document document);

HS2_RMLUI_API void hs2_rmlui_hide_document(hs2_rmlui_document document);

HS2_RMLUI_API void hs2_rmlui_update(
    hs2_rmlui_context context,
    float delta_time);

HS2_RMLUI_API void hs2_rmlui_render(hs2_rmlui_context context);

HS2_RMLUI_API void hs2_rmlui_set_mouse_position(
    hs2_rmlui_context context,
    float x,
    float y);

HS2_RMLUI_API void hs2_rmlui_set_mouse_button(
    hs2_rmlui_context context,
    int32_t button,
    int32_t down);

HS2_RMLUI_API void hs2_rmlui_set_key(
    hs2_rmlui_context context,
    int32_t key,
    int32_t down);

HS2_RMLUI_API void hs2_rmlui_submit_text(
    hs2_rmlui_context context,
    const char* text_utf8);

HS2_RMLUI_API int32_t hs2_rmlui_get_render_data(
    hs2_rmlui_context context,
    hs2_rmlui_render_data* out_render_data);

HS2_RMLUI_API void hs2_rmlui_release_render_data(
    hs2_rmlui_context context);

// Optional but recommended for live gameplay menus. Replaces the contents of
// an already loaded document from generated RML without reloading the document.
HS2_RMLUI_API int32_t hs2_rmlui_set_document_body(
    hs2_rmlui_document document,
    const char* body_rml_utf8);

#ifdef __cplusplus
}
#endif
