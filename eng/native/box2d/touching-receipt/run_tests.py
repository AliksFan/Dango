"""One finite native test per invocation; evidence is never overwritten."""
import argparse,pathlib,subprocess,json
ap=argparse.ArgumentParser();ap.add_argument('--version',required=True);ap.add_argument('--kind',choices=['touching','receipt','endpoint','singleton'],required=True);ap.add_argument('--attempt',type=int,default=1);a=ap.parse_args()
here=pathlib.Path(__file__).resolve().parent;repo=here.parents[3];root=repo/'artifacts/round-material';out=root/'touching-receipt';v=a.version
exe=out/f'build-{v}/Release'/('dango_touching_'+a.kind+'_tests.exe')
first=root/'receipt-history/input-recovery';second=root/'receipt-history/second-failure';third=root/'endpoint-selection/third-failure';fourth=root/'singleton-mode/fourth-failure';managed=fourth/'managed-input'
arguments={
 'touching':[managed/'config-164.bin',managed/'timestep-4.bin',managed/'initial-anchor-8.bin',managed/'controls.bin',fourth/'singleton-damped-state.bin',out/f'touching-{v}-fixed-diagnostic.bin'],
 'receipt':[first/'config-164.bin',first/'fixed-state/input-state-372.bin',first/'old-observed-rows-5865.bin',out/f'receipt-{v}-rows.bin',out/f'receipt-{v}.jsonl'],
 'endpoint':[second/'candidate-failure-diagnostic.bin',second/'candidate-last-healthy-diagnostic.bin'],
 'singleton':[third/'v5-failure-diagnostic-buffer.bin',third/'v5-last-healthy-diagnostic-buffer.bin',out/f'singleton-{v}-fixed-output.bin',second/'candidate-failure-diagnostic.bin']}
c=[str(exe)]+[str(p) for p in arguments[a.kind]];run=subprocess.run(c,capture_output=True)
stem=f'{a.kind}-{v}'+('' if a.attempt==1 else f'-attempt{a.attempt}')
(out/(stem+'.stdout.bin')).open('xb').write(run.stdout);(out/(stem+'.stderr.bin')).open('xb').write(run.stderr)
record={'kind':a.kind,'exit':run.returncode,'command':c};(out/(stem+'-run.json')).open('x',encoding='utf-8').write(json.dumps(record,indent=2)+'\n')
print(a.kind,run.returncode,run.stdout.decode(errors='replace')[-1200:],run.stderr.decode(errors='replace')[-5000:],flush=True)
raise SystemExit(run.returncode)
