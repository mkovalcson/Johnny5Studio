"""Remove frame-only links left behind by the focused updaters.

build_robot.py still generates some parts whose visuals the CAD updaters have
since replaced (painted holes, old pistons and hubs, ridge and spring segments,
the old shoulder plates and swivel). They were kept as empty frames only so
existing fall-apart clips could find them. Fall-apart clips are re-baked from the
current model, so this drops every body link that has no visual or collision,
carries nothing below it and hangs from a fixed joint, together with that joint
(repeating, in case a parent was only holding such links). Run it last; the head
(non-body) links are never touched, and running it again changes nothing.
"""
from pathlib import Path
from hand_cad_details import update


def apply(root):
    removed = []
    while True:
        joints = {j.find('child').get('link'): j for j in root.findall('joint')}
        parents = {j.find('parent').get('link') for j in root.findall('joint')}
        doomed = [l for l in root.findall('link')
                  if l.get('name', '').startswith('body_') and l.find('visual') is None and l.find('collision') is None
                  and l.get('name') not in parents and l.get('name') in joints and joints[l.get('name')].get('type') == 'fixed']
        if not doomed:
            return removed
        for link in doomed:
            root.remove(joints[link.get('name')]); root.remove(link); removed.append(link.get('name'))


if __name__ == '__main__':
    update(Path(__file__).resolve().parent.parent / 'johnny5_head.urdf', apply)
