# MapLibre map controls

Source: https://github.com/maplibre/maplibre-gl-js/tree/3ea36bb61fd93ed9f67e38091bdd8f12948b6cf1

BSD-3-Clause, see LICENSE.txt. The four navigation SVGs are copied; geolocate.svg omits the disabled-state stroke path as the upstream CSS does in its enabled state. All four retain upstream path geometry and set root fill to #e8eaed for the requested dark theme. They are installed into Assets/Resources/Moonkov/UI/MapControls. Control group geometry, surfaces, separators and interaction states from src/css/maplibre-gl.css are adapted to native Unity USS; the original CSS is retained here. No web map engine or webview is introduced.

The complete upstream license is bundled with player builds via Assets/StreamingAssets/ThirdPartyNotices/MapLibre.txt.

Dark palette is a local override of the upstream controls: #202124 surface, #e8eaed foreground, #303134 hover, #8ab4f8 focus/selection. SVG viewport trimming is compensated in USS so the rendered glyph sizes retain upstream proportions.
