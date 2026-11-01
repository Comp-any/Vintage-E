# Third-party notices

## ProjectE

Vintage-E is a Vintage Story port of the Minecraft mod [ProjectE](https://github.com/sinkillerj/ProjectE).
These files are ProjectE's own artwork and sounds, used under its MIT licence (below):

- the Klein Star item textures, `assets/vintagee/textures/item/kleinstar-*.png`;
- ProjectE's textures in `assets/vintagee/textures/projecte/` and sounds in `assets/vintagee/sounds/projecte/`,
  used when the server sets `Look` to `Original` or turns on `ProjectESounds`. They are copied unchanged by
  `tools/import_projecte_assets.py`, except the files in `textures/projecte/derived/`, which that script cuts out of
  ProjectE's condenser, alchemical chest and interdiction torch textures to fit Vintage-E's block models. The sounds
  are renamed after the ProjectE sound events that play them (`peheal.ogg` is `heal.ogg`, and so on).

ProjectE credits the creator of Equivalent Exchange 2, x3n0ph0b3, for allowing the use of EE2's assets, and
MidnightLightning for EE2's GUI textures; some of these textures and sounds go back to EE2.

```
MIT License

Copyright (c) 2020 Sin Tachikawa

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
