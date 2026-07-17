# ZhuaQian Desktop Plugin Catalog
#
# Plugins live in the trusted Plugin Folder (set in Settings -> Plugins).
# Only .py and .ps1 run by default. .exe/.bat/.cmd require "Allow advanced plugins".
#
# Each .json entry below is a manifest describing a starter plugin. The Python/PowerShell
# files are runnable starters you can copy into your Plugin Folder.

plugins:
  - name: "summarize"
    file: "summarize.py"
    desc: "Summarize the piped text into 3 bullet points."
    lang: "python"
  - name: "word_count"
    file: "word_count.py"
    desc: "Count words, characters and lines of the piped text."
    lang: "python"
  - name: "upper"
    file: "upper.ps1"
    desc: "Convert piped text to UPPERCASE (PowerShell starter)."
    lang: "powershell"
