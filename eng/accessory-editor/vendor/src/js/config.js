// Curated local miniPaint configuration; no upstream credentials.
const config={
  "TRANSPARENCY": true,
  "TRANSPARENCY_TYPE": "squares",
  "LANG": "en",
  "WIDTH": 256,
  "HEIGHT": 256,
  "visible_width": null,
  "visible_height": null,
  "COLOR": "#885e3e",
  "ALPHA": 255,
  "ZOOM": 1,
  "SNAP": true,
  "safe_search_can_be_disabled": true,
  "layers": [],
  "layer": null,
  "need_render": false,
  "need_render_changed_params": false,
  "mouse": {},
  "mouse_lock": null,
  "swatches": {
    "default": []
  },
  "user_fonts": {},
  "guides_enabled": false,
  "guides": [],
  "ruler_active": false,
  "enable_autoresize_by_default": false,
  "themes": [
    "light"
  ],
  "FONTS": [
    "Arial",
    "Segoe UI",
    "Microsoft YaHei",
    "Courier New",
    "Times New Roman",
    "Verdana",
    "monospace"
  ],
  "TOOLS": [
    {
      "name": "select",
      "title": "Select object tool",
      "attributes": {
        "auto_select": true
      }
    },
    {
      "name": "brush",
      "attributes": {
        "size": 4,
        "pressure": false
      }
    },
    {
      "name": "erase",
      "on_update": "on_params_update",
      "attributes": {
        "size": 30,
        "circle": true,
        "strict": true
      }
    },
    {
      "name": "magic_erase",
      "title": "Magic Eraser Tool",
      "attributes": {
        "power": 15,
        "anti_aliasing": true,
        "contiguous": false
      }
    },
    {
      "name": "fill",
      "attributes": {
        "power": 5,
        "anti_aliasing": false,
        "contiguous": false
      }
    },
    {
      "name": "text",
      "on_update": "on_params_update",
      "attributes": {
        "font": {
          "value": "Arial",
          "values": [
            "Arial",
            "Segoe UI",
            "Microsoft YaHei",
            "Courier New",
            "Times New Roman",
            "Verdana",
            "monospace"
          ]
        },
        "size": 40,
        "bold": {
          "value": false
        },
        "italic": {
          "value": false
        },
        "underline": {
          "value": false
        },
        "strikethrough": {
          "value": false
        },
        "fill": "#008800",
        "stroke": "#000000",
        "stroke_size": {
          "value": 0,
          "min": 0,
          "step": 0.1
        },
        "kerning": {
          "value": 0,
          "min": -999,
          "max": 999,
          "step": 1
        },
        "leading": {
          "value": 0,
          "min": -999,
          "max": 999,
          "step": 1
        }
      }
    },
    {
      "name": "crop",
      "on_update": "on_params_update",
      "on_leave": "on_leave",
      "attributes": {
        "crop": true
      }
    }
  ],
  "TOOL": {
    "name": "brush",
    "attributes": {
      "size": 4,
      "pressure": false
    }
  }
};
export default config;
