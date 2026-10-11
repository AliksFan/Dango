"""Independent contact/cap accounting from actual exported DLL state and rows."""
import argparse,copy,hashlib,json,math,pathlib,struct

norm=lambda vector:math.sqrt(vector[0]*vector[0]+vector[1]*vector[1])
cross=lambda a,b:a[0]*b[1]-a[1]*b[0]
f32=lambda value:struct.unpack('<f',struct.pack('<f',value))[0]

def contact_ledger(data,stage):
    nodes=data['nodes'];length=data['radialLength'];rounding=128*2**-52 if stage=='double' else 8*2**-23
    expected=[node['input'][:] for node in nodes];scales=[norm(node['input']) for node in nodes]
    normal={};tangent={}
    for row in data['activeRows']:
        if 128<=row['id']<144:normal[row['id']-128]=row['multiplier']
        if 144<=row['id']<160:tangent[row['id']-144]=row['multiplier']
        for term in row['gradients']:
            node=term['node'];factor=row['multiplier']*nodes[node]['inverseMass']
            delta=[factor*value for value in term['gradient']]
            expected[node]=[expected[node][axis]+delta[axis] for axis in range(2)];scales[node]+=norm(delta)
    energy_before=energy_after=mass_sum=impulse_scale=torque_scale=0.
    momentum_before=[0.,0.];momentum_after=[0.,0.];external=[0.,0.]
    angular_before=angular_after=external_torque=0.;velocity=[];origin=nodes[16]['position']
    for index,node in enumerate(nodes):
        mass=1/node['inverseMass'];mass_sum+=mass
        before=[mass*value for value in node['input']];after=[mass*value for value in node[stage]]
        position=[node['position'][axis]-origin[axis] for axis in range(2)]
        momentum_before=[momentum_before[axis]+before[axis] for axis in range(2)]
        momentum_after=[momentum_after[axis]+after[axis] for axis in range(2)]
        energy_before+=.5*mass*sum(value*value for value in node['input'])
        energy_after+=.5*mass*sum(value*value for value in node[stage])
        angular_before+=cross(position,before);angular_after+=cross(position,after)
        impulse_scale+=norm(before)+norm(after);torque_scale+=norm(position)*(norm(before)+norm(after))
        if index<16:
            impulse=[tangent.get(index,0.),normal.get(index,0.)]
            external=[external[axis]+impulse[axis] for axis in range(2)];external_torque+=cross(position,impulse)
            impulse_scale+=norm(impulse);torque_scale+=norm(position)*norm(impulse)
        residual=norm([node[stage][axis]-expected[index][axis] for axis in range(2)])
        allowance=rounding*max(scales[index],length*1e-5)
        velocity.append({'node':index,'residual':residual,'allowance':allowance,'passed':math.isfinite(residual) and residual<=allowance})
    energy_allowance=rounding*max(energy_before,mass_sum*length*length*1e-5*1e-5)
    momentum_residual=norm([momentum_after[axis]-momentum_before[axis]-external[axis] for axis in range(2)])
    momentum_allowance=rounding*max(impulse_scale,mass_sum*length*1e-5)
    angular_residual=abs(angular_after-angular_before-external_torque)
    angular_allowance=rounding*max(torque_scale,mass_sum*length*length*1e-5)
    result={'energy_before':energy_before,'energy_after':energy_after,'energy_allowance':energy_allowance,'momentum_residual':momentum_residual,'momentum_allowance':momentum_allowance,'angular_momentum_residual':angular_residual,'angular_momentum_allowance':angular_allowance,'maximum_relative_velocity_ledger_residual':max(item['residual']/length for item in velocity),'maximum_relative_velocity_ledger_allowance':max(item['allowance']/length for item in velocity),'velocityChecks':velocity}
    result['passed']=all(item['passed'] for item in velocity) and all(math.isfinite(value) for value in result.values() if isinstance(value,float)) and energy_after<=energy_before+energy_allowance and momentum_residual<=momentum_allowance and angular_residual<=angular_allowance
    return result

def cap_ledger(data):
    nodes=data['nodes'];length=data['radialLength'];scale=data['capScale'];origin=nodes[16]['position']
    before_energy=after_energy=mass_sum=before_angular=after_angular=cap_angular=impulse_scale=torque_scale=0.
    before_momentum=[0.,0.];after_momentum=[0.,0.];cap_momentum=[0.,0.]
    exact=True;maximum_before=maximum_after=0.
    speed=lambda values:f32(math.sqrt(f32(f32(values[0]*values[0])+f32(values[1]*values[1]))))
    for node in nodes:
        mass=1/node['inverseMass'];mass_sum+=mass
        exact=exact and [f32(value*scale) for value in node['double']]==node['capped']
        before=[mass*value for value in node['float']];after=[mass*value for value in node['capped']]
        change=[mass*(node['capped'][axis]-node['float'][axis]) for axis in range(2)]
        position=[node['position'][axis]-origin[axis] for axis in range(2)]
        before_momentum=[before_momentum[axis]+before[axis] for axis in range(2)];after_momentum=[after_momentum[axis]+after[axis] for axis in range(2)]
        cap_momentum=[cap_momentum[axis]+change[axis] for axis in range(2)]
        before_energy+=.5*mass*sum(value*value for value in node['float']);after_energy+=.5*mass*sum(value*value for value in node['capped'])
        before_angular+=cross(position,before);after_angular+=cross(position,after);cap_angular+=cross(position,change)
        magnitude=norm(before)+norm(after)+norm(change);impulse_scale+=magnitude;torque_scale+=norm(position)*magnitude
        maximum_before=max(maximum_before,speed(node['float']));maximum_after=max(maximum_after,speed(node['capped']))
    canonical=f32(f32(12*f32(.99999))/maximum_before) if maximum_before>12 else 1.
    energy_allowance=8*2**-23*max(before_energy,mass_sum*length*length*1e-5*1e-5)
    momentum_residual=norm([after_momentum[axis]-before_momentum[axis]-cap_momentum[axis] for axis in range(2)])
    momentum_allowance=128*2**-52*max(impulse_scale,mass_sum*length*1e-5)
    angular_residual=abs(after_angular-before_angular-cap_angular)
    angular_allowance=128*2**-52*max(torque_scale,mass_sum*length*length*1e-5)
    result={'exactSingleRound':exact,'canonicalScale':canonical,'maximumBefore':maximum_before,'maximumAfter':maximum_after,'energyBefore':before_energy,'energyAfter':after_energy,'energyAllowance':energy_allowance,'momentumResidual':momentum_residual,'momentumAllowance':momentum_allowance,'angularResidual':angular_residual,'angularAllowance':angular_allowance}
    result['passed']=exact and canonical==scale and maximum_after<=12 and after_energy<=before_energy+energy_allowance and momentum_residual<=momentum_allowance and angular_residual<=angular_allowance
    return result

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--diagnostic',required=True);parser.add_argument('--layout',required=True);parser.add_argument('--output',required=True)
    args=parser.parse_args();blob=pathlib.Path(args.diagnostic).read_bytes();layout=json.loads(pathlib.Path(args.layout).read_text())
    read=lambda offset,kind='d':struct.unpack_from('<'+kind,blob,offset)[0]
    vector=lambda offset:list(struct.unpack_from('<2d',blob,offset))
    float_vector=lambda offset:list(struct.unpack_from('<2f',blob,offset))
    solver=30008;row_layout=layout['row'];solver_layout=layout['solver'];state=layout['state']
    data={'nodes':[],'activeRows':[],'radialLength':read(solver+state['radial_length'],'f'),'capScale':read(3804,'f')}
    for index in range(17):
        data['nodes'].append({'position':float_vector(solver+state['positions']+index*8),'inverseMass':read(solver+state['inverse_mass']+index*4,'f'),'input':vector(solver+solver_layout['input_velocities']+index*16),'double':vector(solver+solver_layout['velocities']+index*16),'float':float_vector(4932+136+index*8),'capped':float_vector(5304+136+index*8)})
    for index in range(160):
        offset=solver+solver_layout['rows']+index*row_layout['bytes']
        if not read(offset+row_layout['active'],'B'):continue
        count=read(offset+row_layout['count'],'i')
        data['activeRows'].append({'id':read(offset+row_layout['id'],'i'),'multiplier':read(offset+row_layout['multiplier']),'gradients':[{'node':read(offset+row_layout['ids']+term*4,'i'),'gradient':vector(offset+row_layout['gradients']+term*16)} for term in range(count)]})
    result={'kind':'endpoint-independent-actual-dll-ledger','stage':'last accepted substep of bounded second-prefix probe, not the repaired earlier substep','double':contact_ledger(data,'double'),'float':contact_ledger(data,'float'),'cap':cap_ledger(data),'originalMetricEquality':{}}
    # The block finish publishes the final float report into contact.report.
    # No actual double report is exported here; its ledger is recomputed above.
    result['exportedReportStage']='float; the transient double report is not exported'
    for stage,base in [('contactCachedFloat',solver+solver_layout['report']),('blockFinalFloat',95912)]:
        result['originalMetricEquality'][stage]={name:result['float'][name]==read(base+offset) for name,offset in layout['report'].items() if name!='bytes'}
    corrupt=copy.deepcopy(data);corrupt['nodes'][0]['double'][0]+=.001
    result['velocityNegativeRejected']=not contact_ledger(corrupt,'double')['passed']
    result['passed']=result['double']['passed'] and result['float']['passed'] and result['cap']['passed'] and all(all(values.values()) for values in result['originalMetricEquality'].values()) and result['velocityNegativeRejected']
    result['inputHashes']={name:hashlib.sha256(pathlib.Path(path).read_bytes()).hexdigest() for name,path in [('diagnostic',args.diagnostic),('layout',args.layout)]}
    with open(args.output,'x',encoding='utf-8') as stream:json.dump(result,stream,indent=2);stream.write('\n')
    print(json.dumps({name:result[name]['passed'] for name in ['double','float','cap']}));print('passed='+str(result['passed']))
    raise SystemExit(0 if result['passed'] else 1)
if __name__=='__main__':main()
