#!/usr/bin/env python3
# ZhuaQian Desktop plugin starter: summarize.py
# Reads text from STDIN, prints a short 3-bullet summary to STDOUT.
# (Replace the trivial logic with a real call to a local model if you like.)
import sys

def main():
    data = sys.stdin.read()
    if not data.strip():
        print("(no input)")
        return
    words = data.split()
    bullets = []
    # naive split into ~3 chunks as a stand-in for real summarization
    step = max(1, len(words) // 3)
    for i in range(0, len(words), step):
        chunk = " ".join(words[i:i + step])
        if chunk:
            bullets.append("- " + chunk[:160])
    print("\n".join(bullets[:3]))

if __name__ == "__main__":
    main()
