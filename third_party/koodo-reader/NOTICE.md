# Koodo Reader Attribution

This project includes a reader PoC integration path that references the Koodo Reader open-source project:

- Project: https://github.com/koodo-reader/koodo-reader
- License: GNU Affero General Public License v3.0 (AGPL-3.0)
- License file: `third_party/koodo-reader/LICENSE-AGPL-3.0.txt`

Bundled runtime assets used for PoC experiments are stored under:

- `frontend/public/vendor/koodo/kookit.min.js`
- `frontend/public/vendor/koodo/kookit-extra-browser.min.js`

The integration in this repository keeps a replaceable adapter boundary (`KoodoAdapter`) so the rendering backend can be swapped later without changing Reader APIs.
