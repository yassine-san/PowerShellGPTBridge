"""Production MCP endpoint with explicitly simulated UI/worker/SSH, NOT Windows E2E."""
import subprocess,json,os,sys,threading,queue,time
from pathlib import Path
dotnet=sys.argv[1] if len(sys.argv)>1 else 'dotnet'
assembly=str(Path(__file__).parent/'bin/Debug/net8.0/Protocol.dll')
results=[]
def run(elevated=False, modern=False, timing=None, restricted=True, shell='powershell'):
 tool,pin=('execute_powershell','Get-Date') if shell=='powershell' else ('execute_shell','date')
 env=os.environ.copy();env['PSBRIDGE_SHELL']=shell;env['TEST_ELEVATED']='1' if elevated else '0'
 env['PSBRIDGE_ALLOW_ELEVATED_GET_DATE']='1' if elevated and restricted else '0'
 p=subprocess.Popen([dotnet,assembly],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,env=env)
 output=queue.Queue()
 def reader():
  for line in p.stdout: output.put(json.loads(line))
 threading.Thread(target=reader,daemon=True).start()
 def send(method,params,id=None):
  if modern: params['_meta']={'io.modelcontextprotocol/protocolVersion':'2026-07-28','io.modelcontextprotocol/clientCapabilities':{},'io.modelcontextprotocol/clientInfo':{'name':'probe','version':'1'}}
  msg=dict(jsonrpc='2.0',method=method,params=params)
  if id is not None:msg['id']=id
  p.stdin.write(json.dumps(msg).encode()+b'\n');p.stdin.flush()
 def recv():
  v=output.get(timeout=75 if timing else 15);assert 'error' not in v,v;return v
 n=10
 def call(command,**kwargs):
  nonlocal n;n+=1
  send('tools/call',{'name':tool,'arguments':dict(command=command,**kwargs)},n)
  return recv()['result']['structuredContent']
 def check(name,condition):
  results.append(dict(name=name,protocol='2026-07-28' if modern else '2025-11-25',elevated=elevated,status='PASS' if condition else 'FAIL',environment='simulated UI/worker, real endpoint and MCP SDK',shell=shell))
  assert condition,name
 try:
  if modern: send('server/discover',{},1);recv()
  else: send('initialize',dict(protocolVersion='2025-11-25',capabilities={},clientInfo=dict(name='probe',version='1')),1);recv();send('notifications/initialized',{})
  send('tools/list',{},2);ts=recv()['result']['tools'];check('discovery both tools write annotations',len(ts)==2 and all(t['annotations']['readOnlyHint']==False for t in ts))
  check('discovery names the '+shell+' tool',tool in [t['name'] for t in ts] and 'ssh_operation' in [t['name'] for t in ts])
  if timing:
   start=time.monotonic();r=call(timing,timeout_seconds=60);elapsed=time.monotonic()-start
   if timing=='clock-expiry':
    check('real 60s endpoint timer expires with draft and no dispatch',59.8<=elapsed<=64 and r['status']=='approval_expired' and r['incomplete_feedback']=='unfinished' and not r['execution_attempted'])
   else:
    check('late approval declines insufficient execution budget',r['status']=='insufficient_time' and r['approval']=='approved' and not r['execution_attempted'])
  elif elevated and not restricted:
   r=call('approved');check('configured elevated general mode still requires approval',r['approval']=='approved' and r['privilege']=='ELEVATED — ADMINISTRATOR')
  elif elevated:
   r=call('Get-Date; whoami');check('elevated prototype rejects arbitrary script',not r['executed'])
   r=call('Get-Date',elevated=True);check('elevated prototype rejects broker escape',not r['executed'])
  else:
   r=call('before\x00after');r2=call('before\u202eafter');check('hidden control characters refused',not r['executed'] and not r2['executed'])
   r=call('reject');check('rejection reason and zero execution',r['reason']=='Keep the backup chain' and not r['executed'])
   r=call('expired');check('expired draft is separate from deliberate rejection',r['status']=='approval_expired' and r['incomplete_feedback']=='half a sentence' and r['reason']=='' and not r['execution_attempted'])
   r=call('ui-failure');check('UI failure prevents execution',not r['executed'])
   r=call('approved');check('approved once',r['stdout']=='1' and r['executed'])
   r2=call('approved');check('repeat fresh approval and same session',r2['stdout']=='2' and r['session_id']==r2['session_id'] and r['request_id']!=r2['request_id'])
   r=call('modified');check('modified gets new approval',r['stdout']=='3')
   r=call('approved',timeout_seconds=61);check('timeout bounds reject before execution',not r['executed'])
   r=call(pin);check('fixed date classified READ',r['classification']=='READ' and r['status']=='completed')
   r=call(pin+'; whoami');check('arbitrary commands classified WRITE',r['classification']=='WRITE')
   send('tools/call',{'name':tool,'arguments':{'command':'busy-one'}},90)
   send('tools/call',{'name':tool,'arguments':{'command':'busy-two'}},91)
   pair=[recv()['result']['structuredContent'] for _ in range(2)];check('overlap rejects one',sum(x['executed'] for x in pair)==1)
   r=call('stop');check('stop task cancellation reported',r['task_cancelled'] and not r['executed'])
   r=call('approved');check('stop task latches against subsequent request',r['task_cancelled'] and not r['executed'])
  p.stdin.close();check('EOF terminates server',p.wait(timeout=10)==0)
 finally:
  if p.poll() is None:p.kill()
if len(sys.argv)>2: run(timing=sys.argv[2])
else:
 run();run(True);run(modern=True);run(True,True);run(True,restricted=False)
 # The bash profile exists only on Linux: Windows always uses PowerShell and the bridge refuses PSBRIDGE_SHELL=bash there.
 if os.name!='nt': run(shell='bash');run(shell='bash',modern=True)
print(json.dumps(results,indent=2))
