"""Preview retention actions by default. --apply performs only guarded actions.
Completed releases are moved intact to Archive, never recursively deleted.
Only explicitly marked, generated packaging folders can be deleted.
"""
from pathlib import Path
import argparse,datetime,json,os,re,shutil,stat
ROOT=Path(__file__).resolve().parents[1]
MARKER='.j5-generated.json'
def mark(path,kind,version=''):
    path=Path(path).resolve()
    if not path.is_relative_to(ROOT/'Johnny5VR/packaging') and not path.is_relative_to(ROOT/'ServoAnimator/DevelopmentArtifacts/Packaging'):
        raise ValueError('Generated folder must be in a recognized packaging area')
    (path/MARKER).write_text(json.dumps({'kind':kind,'version':version}),encoding='utf-8')
def plain_tree(path):
    if path.lstat().st_file_attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT:raise ValueError('Refusing a reparse point')
    for base,dirs,files in os.walk(path):
        for name in dirs+files:
            if (Path(base)/name).lstat().st_file_attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT:raise ValueError('Refusing a tree containing links')
def actions(root,policy):
    result=[]
    for flavor in ('Desktop','VR'):
        home=root/'Releases'/flavor
        versions=sorted([p for p in home.iterdir() if p.is_dir() and re.fullmatch(r'\d+\.\d+\.\d+',p.name)],key=lambda p:tuple(map(int,p.name.split('.'))),reverse=True) if home.exists() else []
        for p in versions[policy['releaseVersionsToKeep']:]:
            target=root/'Archive/RetiredReleases'/flavor/p.name
            if not target.exists():result.append(('archive-release',p,target))
    packaging=root/'Johnny5VR/packaging'
    for prefix in ('publish','editor'):
        candidates=[]
        for p in packaging.glob(prefix+'-*'):
            if not p.is_dir() or not (p/MARKER).is_file():continue
            info=json.loads((p/MARKER).read_text())
            if info.get('kind')=='publish' and re.fullmatch(r'\d+\.\d+\.\d+',info.get('version','')):
                candidates.append((tuple(map(int,info['version'].split('.'))),p))
        for version,p in sorted(candidates,reverse=True)[policy['publishedVersionsToKeep']:]:
            if not (p/'Config').exists() and not (p/'Settings').exists():result.append(('remove-generated',p,None))
    cutoff=datetime.datetime.now().timestamp()-policy['stagingMaxAgeDays']*86400
    for scope in (packaging/'staging',packaging/'verification',root/'ServoAnimator/DevelopmentArtifacts/Packaging'):
        if not scope.exists():continue
        for p in scope.iterdir():
            marker=p/MARKER
            if p.is_dir() and marker.is_file() and marker.stat().st_mtime<cutoff:
                info=json.loads(marker.read_text())
                if info.get('kind') in ('stage','verification'):result.append(('remove-generated',p,None))
    return result
def main():
    parser=argparse.ArgumentParser();parser.add_argument('--apply',action='store_true');args=parser.parse_args()
    policy=json.loads((ROOT/'tools/retention-policy.json').read_text())
    if min(policy['releaseVersionsToKeep'],policy['publishedVersionsToKeep'],policy['stagingMaxAgeDays'])<1:raise ValueError('Retention values must be positive')
    report=[]
    for kind,path,target in actions(ROOT,policy):
        path=path.resolve()
        if not path.is_relative_to(ROOT) or path==ROOT:raise ValueError('Unsafe retention path')
        if args.apply:
            plain_tree(path)
            if target:
                target=target.resolve()
                if not target.is_relative_to(ROOT/'Archive/RetiredReleases') or target.exists():raise ValueError('Unsafe archive destination')
                target.parent.mkdir(parents=True,exist_ok=True);os.rename(path,target)
            else:
                allowed=(ROOT/'Johnny5VR/packaging',ROOT/'ServoAnimator/DevelopmentArtifacts/Packaging')
                if not any(path.is_relative_to(scope) and path!=scope for scope in allowed) or not (path/MARKER).is_file():raise ValueError('Unmarked cleanup target')
                shutil.rmtree(path)
        report.append({'action':kind,'source':str(path),'destination':str(target) if target else None})
    print(json.dumps({'applied':args.apply,'actions':report},indent=2))
if __name__=='__main__':main()
