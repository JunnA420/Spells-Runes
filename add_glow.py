import re, sys

path = input("File path: ").strip().strip('"') if len(sys.argv) < 2 else sys.argv[1]

with open(path, "r", encoding="utf-8") as f:
    text = f.read()

count = 0

def add_glow(m):
    global count
    if "glow" not in m.group(0):
        count += 1
        return m.group(0)[:-1] + ', "glow": 255}'
    return m.group(0)

text = re.sub(r'\{[^{}]*"texture":\s*"#[^"]*emissive[^"]*"[^{}]*\}', add_glow, text)

with open(path, "w", encoding="utf-8") as f:
    f.write(text)

print(f"Added glow to {count} faces in {path}")
