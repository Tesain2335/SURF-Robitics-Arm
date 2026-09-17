"""Independent ROS-convention FK oracle from XML, no Unity transform math."""
from pathlib import Path
import xml.etree.ElementTree as ET
import numpy as np
import json
project=Path(__file__).resolve().parents[1]
(project/'Library').mkdir(exist_ok=True)
root=ET.parse(project/'Assets/PiperRobot/piper_description.urdf').getroot()
joints=[root.find(f"joint[@name='joint{i}']") for i in range(1,7)]
def rotate(axis,angle):
    axis=np.array(axis,dtype=float);axis/=np.linalg.norm(axis)
    x,y,z=axis;c=np.cos(angle);s=np.sin(angle)
    k=np.array([[0,-z,y],[z,0,-x],[-y,x,0]])
    return np.eye(3)*c+(1-c)*np.outer(axis,axis)+s*k
def fk(q):
    t=np.eye(4)
    for j,a in zip(joints,q):
        o=j.find('origin');r,p,y=map(float,o.attrib['rpy'].split())
        m=np.eye(4);m[:3,3]=list(map(float,o.attrib['xyz'].split()))
        m[:3,:3]=rotate([0,0,1],y)@rotate([0,1,0],p)@rotate([1,0,0],r)@rotate(list(map(float,j.find('axis').attrib['xyz'].split())),a)
        t=t@m
    def convert(v):
        x,y,z=v
        return {'x':-y,'y':z,'z':x}
    return dict(tip=convert(t[:3,3]),basisX=convert(t[:3,0]),basisY=convert(t[:3,1]),basisZ=convert(t[:3,2]))
cases=[]
for i,j in enumerate(joints):
    lo=float(j.find('limit').attrib['lower']);hi=float(j.find('limit').attrib['upper'])
    for value in [lo,0,(lo+hi)/2,hi]:
        q=[0.]*6;q[i]=value;cases.append(dict(q=q,**fk(q)))
rng=np.random.default_rng(1509)
for n in range(30):
    q=[float(rng.uniform(float(j.find('limit').attrib['lower']),float(j.find('limit').attrib['upper']))) for j in joints]
    cases.append(dict(q=q,**fk(q)))
(project/'Library/surf-fk-cases.json').write_text(json.dumps({'cases':cases}),encoding='utf-8')
print(len(cases),'independent FK cases')
