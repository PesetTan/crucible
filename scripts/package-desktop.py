#!/usr/bin/env python3
"""Create a locally signed macOS Tauri bundle from already verified native runtimes."""
import argparse,os,plistlib,shutil,subprocess,tempfile
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--ui-runtime',type=Path,required=True);p.add_argument('--backend-runtime',type=Path,required=True);p.add_argument('--output',type=Path,default=Path('dist/Crucible.app'));args=p.parse_args();root=Path(__file__).resolve().parents[1];binary=root/'src/Crucible.Ui/src-tauri/target/release/crucible-desktop'
for runtime,name in [(args.ui_runtime,'CrucibleUi'),(args.backend_runtime,'CrucibleBackend')]:
 result=subprocess.check_output([str(runtime.resolve()/name),'--check-runtime'],text=True)
 if 'native-aot' not in result:raise SystemExit(f'{name} is not a Native AOT host')
if not binary.is_file():raise SystemExit('Build the release Tauri executable first.')
with tempfile.TemporaryDirectory(prefix='crucible-desktop-package-',dir='/private/tmp') as temporary:
 app=Path(temporary)/'Crucible.app';contents=app/'Contents';(contents/'MacOS').mkdir(parents=True);resources=contents/'Resources';(resources/'resources').mkdir(parents=True)
 shutil.copy2(binary,contents/'MacOS/Crucible');shutil.copy2(root/'src/Crucible.Ui/src-tauri/icons/icon.icns',resources/'Crucible.icns')
 for notice in ['LICENSE','THIRD_PARTY.md']:
  shutil.copy2(root/notice,resources/notice)
 (resources/'docs').mkdir()
 shutil.copy2(root/'docs/license-review.md',resources/'docs/license-review.md')
 for runtime,name in [(args.ui_runtime,'ui'),(args.backend_runtime,'service')]:
  shutil.copytree(runtime,resources/'resources'/name,ignore=shutil.ignore_patterns('*.dSYM','*.pdb','*.dbg'))
 (resources/'resources/project-root.txt').write_text(str(root)+'\n')
 info={'CFBundleExecutable':'Crucible','CFBundleIdentifier':'local.crucible.desktop','CFBundleName':'Crucible','CFBundleDisplayName':'Crucible','CFBundlePackageType':'APPL','CFBundleShortVersionString':'0.1.0','CFBundleVersion':'1','CFBundleIconFile':'Crucible.icns','LSMinimumSystemVersion':'13.0','NSHighResolutionCapable':True,'NSLocalNetworkUsageDescription':'Crucible connects to its local simulation service on this computer.'}
 with open(contents/'Info.plist','wb') as f:plistlib.dump(info,f)
 subprocess.run(['xattr','-cr',str(app)],check=True);subprocess.run(['codesign','--force','--deep','--sign','-',str(app)],check=True);subprocess.run(['codesign','--verify','--deep','--strict',str(app)],check=True)
 output=args.output.resolve();output.parent.mkdir(parents=True,exist_ok=True)
 if output.exists():raise SystemExit(f'Refusing to overwrite {output}; choose a new output directory.')
 subprocess.run(['ditto','--norsrc','--noextattr',str(app),str(output)],check=True)
 print(output)
