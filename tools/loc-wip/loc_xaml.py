"""Adds x:Uid to XAML elements with literal UI text and collects resource keys. Run with --apply to write files."""
import os, re, sys, json
ROOT = r"D:\Projects\Nax-TaskManager\src\BetterTaskManager.Fluent"
OUT = r"C:\Temp\loc_xaml.json"
apply = '--apply' in sys.argv

PLAIN = ['Text', 'Label', 'Content', 'PlaceholderText', 'Title', 'Message', 'OnContent', 'OffContent', 'Header']
ATTACHED = {
    'ToolTipService.ToolTip': '[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip',
    'AutomationProperties.Name': '[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name',
}
KEEP = {'Nax-TaskManager', 'CPU', 'TCP', 'UDP'}  # product name and protocol names stay as they are

files = ['MainWindow.xaml'] + [os.path.join('Views', f) for f in sorted(os.listdir(os.path.join(ROOT, 'Views'))) if f.endswith('.xaml')]
entries = []
for rel in files:
    path = os.path.join(ROOT, rel)
    text = open(path, encoding='utf-8').read()
    prefix = os.path.splitext(os.path.basename(rel))[0]
    counter = [0]

    def handle(match):
        tag = match.group(0)
        if 'x:Uid=' in tag:
            return tag
        found = []
        for attr in PLAIN + list(ATTACHED):
            m = re.search(r'(?<![\w.])' + re.escape(attr) + r'="([^"{][^"]*)"', tag)
            if m and m.group(1).strip() and m.group(1) not in KEEP and re.search('[A-Za-z]{2,}', m.group(1)):
                found.append((attr, m.group(1)))
        if not found:
            return tag
        counter[0] += 1
        uid = f"{prefix}_{counter[0]}"
        for attr, value in found:
            key = f"{uid}.{ATTACHED.get(attr, attr)}"
            entries.append({'file': rel, 'key': key, 'en': value.replace('&quot;', '"').replace('&amp;', '&')})
        element = re.match(r'<[\w:.]+', tag).group(0)
        return tag.replace(element, f'{element} x:Uid="{uid}"', 1)

    new = re.sub(r'<[\w:.]+\s[^<>]*?/?>', handle, text, flags=re.S)
    if apply and new != text:
        open(path, 'w', encoding='utf-8').write(new)

json.dump(entries, open(OUT, 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
for e in entries:
    print(f"{e['key']:<70} {e['en'][:90]}")
print(len(entries), "entries")
