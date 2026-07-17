#!/usr/bin/env python3
# ZhuaQian Desktop plugin starter: word_count.py
# Reads text from STDIN, prints word/char/line counts to STDOUT.
import sys

def main():
    data = sys.stdin.read()
    lines = data.count("\n") + (1 if data and not data.endswith("\n") else 0)
    words = len(data.split())
    chars = len(data)
    print("lines=%d words=%d chars=%d" % (lines, words, chars))

if __name__ == "__main__":
    main()
