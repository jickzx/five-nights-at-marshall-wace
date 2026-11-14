"""Rebuild current beast source in memory and inspect every baked motion frame."""
import bpy,sys,math,json,os,hashlib
from pathlib import Path
root=Path(__file__).resolve().parents[2]
out=Path(os.environ.get('ART_OUTPUT_ROOT',root))/'assets/previews'
out.mkdir(parents=True,exist_ok=True)
sys.path.insert(0,str(root/'tools/blender'))
import creature
creature.deliver=lambda *args,**kwargs:None
report={'blender_version':bpy.app.version_string,
        'source_sha256':{str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest()
                         for p in [root/'tools/blender/creature.py',root/'tools/blender/common.py']},
        'method':'Rebuilt each stage from source in memory; evaluated skinned vertices and pose matrices at every baked frame. Foot clearance is measured against world Z=0. Stance phases use the gait source schedule.',
        'models':{}}
for stage in [1,2,3]:
    creature.build_creature(stage)
    name=f'beast_stage{stage}';r=bpy.data.objects[name+'_rig'];obj=bpy.data.objects[name+'_mesh'];scene=bpy.context.scene
    ids={g.name:[v.index for v in obj.data.vertices if any(w.group==g.index for w in v.groups)] for g in obj.vertex_groups}
    model={'sampled_frames':0,'clips':{}}
    for clip in ['idle','stalk','chase','lunge']:
        act=bpy.data.actions[clip];r.animation_data.action=act
        rec={'frames':int(act.frame_range[1]),'duration_seconds':(act.frame_range[1]-1)/24,
             'lowest_vertex':{'z_m':100},'max_stance_toe_height_m':0,
             'max_joint_gap_m':0,'lunge_final_feet_m':{},'both_feet_above_2cm_frames':[]}
        start=None
        for frame in range(1,int(act.frame_range[1])+1):
            scene.frame_set(frame);ev=obj.evaluated_get(bpy.context.evaluated_depsgraph_get());verts=ev.data.vertices
            low=min(verts,key=lambda v:v.co.z)
            if low.co.z<rec['lowest_vertex']['z_m']:
                rec['lowest_vertex']={'z_m':low.co.z,'frame':frame,'vertex':low.index,
                                      'bones':[obj.vertex_groups[g.group].name for g in obj.data.vertices[low.index].groups]}
            t=(frame-1)/(int(act.frame_range[1])-1)
            feet={side:min(verts[i].co.z for i in ids['foot.'+side]) for side in ['L','R']}
            if min(feet.values())>.02:rec['both_feet_above_2cm_frames'].append(frame)
            if clip in ['stalk','chase']:
                for side,offset in [('L',0),('R',.54)]:
                    if (t+offset)%1 < (.43 if clip=='chase' else .70):
                        if feet[side]>rec['max_stance_toe_height_m']:
                            rec['max_stance_toe_height_m']=feet[side]
                            rec['max_stance_location']={'frame':frame,'bone':'foot.'+side}
            if clip=='lunge' and frame in [35,37]:rec['lunge_final_feet_m'][str(frame)]=feet
            if clip=='chase' and frame==18:
                rec['frame18_hand_min_z_m']={side:min(verts[i].co.z for i in ids['hand.'+side]) for side in ['L','R']}
            for bone in r.pose.bones:
                if bone.parent and (bone.bone.head_local-bone.parent.bone.tail_local).length < .0001:
                    gap=(bone.head-bone.parent.tail).length
                    if gap>rec['max_joint_gap_m']:
                        rec['max_joint_gap_m']=gap;rec['max_joint_location']={'frame':frame,'bone':bone.name}
            if frame==1:start=[p.matrix.copy() for p in r.pose.bones]
        rec['end_to_start_matrix_difference']=max(abs(p.matrix[i][j]-m[i][j]) for p,m in zip(r.pose.bones,start) for i in range(4) for j in range(4))
        rec['loop']=clip!='lunge'
        assert rec['lowest_vertex']['z_m'] > -.002, (name,clip,'ground penetration',rec['lowest_vertex'])
        assert rec['max_stance_toe_height_m'] < .002, (name,clip,'floating stance')
        assert rec['max_joint_gap_m'] < .001, (name,clip,'joint separation')
        if rec['loop']:
            assert rec['end_to_start_matrix_difference'] < .0001, (name,clip,'loop discontinuity')
        rec['status']='passed'
        model['sampled_frames']+=rec['frames'];model['clips'][clip]=rec
        print('AUDIT',name,clip,'ground',rec['lowest_vertex'],'stance',rec['max_stance_toe_height_m'],'joint',rec['max_joint_gap_m'])
    report['models'][name]=model
report['sampled_frames']=sum(m['sampled_frames'] for m in report['models'].values())
(out/'beast_motion_validation.json').write_text(json.dumps(report,indent=2)+'\n')
