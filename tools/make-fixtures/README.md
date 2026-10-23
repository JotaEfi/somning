# Portuguese speech fixtures

Run `powershell -NoProfile -ExecutionPolicy Bypass -File tools/make-fixtures/make_fixtures.ps1` from any directory. The generator uses `-EdgeTts <path>`, then `tools/make-fixtures/.venv/Scripts/edge-tts.exe`, then the research spike executable at `C:\somning-spike\.venv\Scripts\edge-tts.exe`. If none is available, it uses the installed SAPI voice **Microsoft Maria Desktop**. Pass `-UseSapi` to select that built-in fallback explicitly. It never installs packages.

Edge TTS is flagged for package legitimacy review in the project research. Anyone considering an install must first verify the package and publisher at [pypi.org/project/edge-tts](https://pypi.org/project/edge-tts/). `requirements.txt` pins the research version only; it does not authorize installation.

Edge TTS speech is synthesized at -10% rate; SAPI speech uses a modestly slower rate. The script converts all fixtures to 16 kHz, mono, signed 16-bit PCM WAV. It uses FFmpeg for conversion, the -20 dB quiet variant, pink-noise mixing, the 0.2-second tap, silence, noise and a 440 Hz tone. The expected text and generation method for every fixture are recorded in `tests/fixtures/manifest.json`.
