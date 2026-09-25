# Sterling Software Centre artwork

Original geometric artwork created for this repository. Editable paths and colours are in `icon-pack.json`; generated individual SVGs remain directly editable in a vector editor. Change the JSON to keep every export consistent. `splash.svg` is the editable composition; the WPF splash uses live text, version and progress in the same composition.

- `app`: angular S with package tiles; executable, window, splash and header. ICO contains 16, 24, 32, 48, 64, 128 and 256 px 32-bit PNG images.
- `install`, `update`, `uninstall`: package operations.
- `capture`, `restore`, `data`, `lists`: inventory bundles, recovery, reviewed application data and deployment lists.
- `devices`, `service`: reserved assets for future remote management; these features are not implied to exist.
- `settings`, `success`, `warning`, `failure`: configuration and status. Always pair status colour with readable text.

Palette: midnight navy `#102D46`, teal `#177F85`, mint `#3DE0C3`, amber `#FFC86B`; success `#087F70`, warning `#A76500`, failure `#C33B56`. Navigation icons use 24-unit canvases and 1.8-unit rounded strokes. Most UI uses vector DrawingImages for DPI scaling; 48 px PNGs and the 256 px app PNG are also supplied.

Regenerate on Windows with .NET 10: `dotnet run --project build/IconExporter -- .` from the repository root. It writes SVG/PNG/ICO/Icons.xaml; no external image service, font download or third-party icon library is used. Keep the generated files committed so ordinary builds do not need to run the exporter.
