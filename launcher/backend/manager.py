"""Own only launcher-created processes; never kill a user's existing ROS nodes."""
import fcntl
import json
import math
import os
from pathlib import Path
import signal
import socket
import subprocess
import sys
import time

ROOT = Path(os.environ.get('SURF_WS', str(Path(__file__).resolve().parents[1])))
STATE = Path.home() / '.local/state/surf-launcher'
STATE.mkdir(parents=True, exist_ok=True)
BOOT = Path('/proc/sys/kernel/random/boot_id').read_text().strip()


def identity(pid):
    try:
        fields = Path(f'/proc/{pid}/stat').read_text().rsplit(')', 1)[1].split()
        return None if fields[0] == 'Z' else fields[19]
    except (OSError, IndexError):
        return None


def read(name):
    try:
        data = json.loads((STATE / (name + '.json')).read_text())
        if data['boot'] == BOOT and identity(data['pid']) == data['start']:
            return data
        if data['boot'] == BOOT and identity(data['pid']) is None:
            if any(identity(int(pid)) == start for pid, start in data.get('members', {}).items()):
                return data
    except (OSError, ValueError, KeyError):
        pass
    return None


def status():
    services = {n: read(n) for n in ('endpoint', 'bridge', 'driver', 'rviz')}
    ip = subprocess.check_output(['hostname', '-I'], text=True).split()[0]
    try:
        info = can_info()
        can = 'UP / 1 Mbps'
    except RuntimeError as error:
        can = str(error)
    return {'ok': True, 'ip': ip, 'services': services, 'logs': str(STATE), 'can': can,
            'mode': (services['bridge'] or {}).get('mode', 'stopped')}


def foreign_processes():
    # Compare process groups so children of ros2 launch are treated as owned.
    groups = {r['pid'] for n in ('endpoint', 'bridge', 'driver', 'rviz') if (r := read(n))}
    found = []
    markers = ('unity_servo.py', 'unity_piper_bridge.py', 'gripper_bridge',
               'agx_arm_ctrl_single', 'default_server_endpoint', 'unity_to_moveit.py')
    for p in Path('/proc').glob('[0-9]*'):
        try:
            args = (p / 'cmdline').read_bytes().replace(b'\0', b' ').decode(errors='replace')
            if any(m in args for m in markers) and os.getpgid(int(p.name)) not in groups:
                # Shell command text is not an actual service executable.
                exe = (p / 'comm').read_text().strip()
                if exe not in ('bash', 'sh', 'timeout'):
                    found.append({'pid': int(p.name), 'command': args[:180]})
        except (OSError, ProcessLookupError):
            pass
    return found


def check_conflicts():
    other = foreign_processes()
    if other:
        raise RuntimeError('检测到不是启动器管理的旧 ROS 服务，请先在原终端停止它们：' + json.dumps(other, ensure_ascii=False))


def start(name, args, mode=''):
    if read(name):
        return
    with (STATE / (name + '.log')).open('w') as log:
        proc = subprocess.Popen(args, cwd=ROOT, stdin=subprocess.DEVNULL,
                                stdout=log, stderr=log, start_new_session=True)
    data = {'pid': proc.pid, 'start': identity(proc.pid), 'boot': BOOT, 'mode': mode}
    (STATE / (name + '.json')).write_text(json.dumps(data))
    time.sleep(0.25)
    if not read(name):
        raise RuntimeError(name + ' 启动退出：' + tail(name))


def tail(name):
    path = STATE / (name + '.log')
    if not path.exists():
        return ''
    with path.open('rb') as stream:
        stream.seek(max(0, path.stat().st_size - 5000))
        return stream.read().decode(errors='replace')


def wait_bridge(dry_run):
    """Ask the ROS node, not a startup line that disappears from a busy log."""
    expected = 'Boolean value is: ' + str(dry_run)
    deadline = time.monotonic() + 15
    last = ''
    while time.monotonic() < deadline:
        if not read('bridge'):
            raise RuntimeError('桥接进程已退出：' + tail('bridge'))
        try:
            probe = subprocess.run(['ros2', 'param', 'get', '/unity_piper_bridge', 'dry_run'],
                                   capture_output=True, text=True, timeout=5)
            last = probe.stdout + probe.stderr
            if probe.returncode == 0 and expected in probe.stdout:
                return
        except subprocess.TimeoutExpired:
            last = 'ROS 参数服务响应超时'
        time.sleep(0.2)
    raise RuntimeError('桥接节点模式未确认：' + last + '\n' + tail('bridge'))


def stop(name):
    record = read(name)
    if not record:
        return
    # Remember verified children BEFORE the ros2 launch leader exits. PID start
    # times let retries identify our surviving children without trusting names.
    members = dict(record.get('members', {}))
    for path in Path('/proc').glob('[0-9]*'):
        pid = int(path.name)
        try:
            if os.getpgid(pid) == record['pid']:
                stamp = identity(pid)
                if stamp is not None:
                    members[str(pid)] = stamp
        except ProcessLookupError:
            pass
    record['members'] = members
    (STATE / (name + '.json')).write_text(json.dumps(record))
    # Escalation is limited to recorded PID/start-time identities, never a
    # process-name match or an unverified user service.
    for sig, wait in ((signal.SIGINT, 3), (signal.SIGTERM, 2), (signal.SIGKILL, 1)):
        if not read(name):
            break
        for pid, stamp in members.items():
            if identity(int(pid)) == stamp:
                try:
                    os.kill(int(pid), sig)
                except ProcessLookupError:
                    pass
        end = time.monotonic() + wait
        while read(name) and time.monotonic() < end:
            time.sleep(0.1)
    if read(name):
        raise RuntimeError(name + ' 未正常退出，保留状态供检查；没有强制杀死其他进程')


def port_ready():
    try:
        with socket.create_connection(('127.0.0.1', 10000), timeout=0.3):
            return True
    except OSError:
        return False


def develop():
    check_conflicts()
    if read('bridge') and read('bridge')['mode'] == 'live':
        raise RuntimeError('实机控制正在运行；请先停止服务，再切换开发模式')
    if not read('endpoint'):
        if port_ready():
            raise RuntimeError('端口 10000 被其他程序占用，未覆盖它')
        start('endpoint', ['ros2', 'run', 'ros_tcp_endpoint', 'default_server_endpoint',
                          '--ros-args', '-p', 'ROS_IP:=0.0.0.0'])
    deadline = time.monotonic() + 15
    while not port_ready():
        if not read('endpoint') or time.monotonic() > deadline:
            raise RuntimeError('ROS TCP 未就绪：' + tail('endpoint'))
        time.sleep(0.3)
    if not read('bridge'):
        start('bridge', ['python3', '-u', str(ROOT / 'unity_piper_bridge.py'),
                         '--ros-args', '-p', 'dry_run:=true'], 'dry-run')
    wait_bridge(True)


def can_info():
    p = subprocess.run(['ip', '-details', '-json', 'link', 'show', 'can0'],
                       text=True, capture_output=True)
    if p.returncode:
        raise RuntimeError('没有 can0，请连接 USB-CAN 后使用“挂载 USB-CAN”')
    info = json.loads(p.stdout)[0]
    data = info.get('linkinfo', {}).get('info_data', {})
    if 'UP' not in info.get('flags', []) or data.get('bittiming', {}).get('bitrate') != 1000000:
        raise RuntimeError('can0 未配置为 UP / 1 Mbps，请使用“挂载 USB-CAN”')
    if data.get('state') in ('BUS-OFF', 'STOPPED', 'ERROR-PASSIVE'):
        raise RuntimeError('CAN 状态异常：' + str(data.get('state')))
    return info


def require_can_traffic():
    # Passive receive only. Do not enable or send test motion to diagnose wiring.
    with socket.socket(socket.AF_CAN, socket.SOCK_RAW, socket.CAN_RAW) as bus:
        bus.bind(('can0',))
        bus.settimeout(3.0)
        try:
            bus.recv(16)
        except socket.timeout:
            raise RuntimeError('USB-CAN 已挂载，但 3 秒内没有收到 CAN 帧。'
                               '请检查 Piper 独立电源、急停状态和 CAN 线连接；'
                               '尚未请求使能，重试不会自动移动机械臂。')


def live():
    check_conflicts()
    can_info()
    if read('bridge') and read('bridge')['mode'] == 'live':
        if not read('driver'):
            stop('bridge')
            raise RuntimeError('实机驱动已退出，已停止旧控制桥；请重新启动实机')
        wait_bridge(False)
        return
    require_can_traffic()
    develop()
    try:
        start_driver()
        # Ready check and explicit enabling are performed only by the LIVE action.
        result = subprocess.run(['python3', str(Path(__file__).with_name('robot_ready.py'))],
                                text=True, capture_output=True, timeout=50)
        if result.returncode:
            raise RuntimeError(result.stdout + result.stderr + '\n' + tail('driver'))
        stop('bridge')
        start('bridge', ['python3', '-u', str(ROOT / 'unity_piper_bridge.py'),
                         '--ros-args', '-p', 'dry_run:=false'], 'live')
        wait_bridge(False)
    except Exception:
        # Stop new command generation on partial failure; do not disable motors
        # or open a loaded gripper automatically.
        stop('bridge')
        stop('driver')
        raise


def start_driver():
    start('driver', ['ros2', 'launch', 'agx_arm_ctrl', 'start_single_agx_arm.launch.py',
                     'can_port:=can0', 'arm_type:=piper', 'effector_type:=agx_gripper',
                     'auto_enable:=false', 'control_enabled:=false',
                     'speed_percent:=75', 'fast_mode:=false', 'enable_timeout:=15.0',
                     'gripper_default_effort:=0.5'])


def inspect_robot():
    check_conflicts()
    can_info()
    require_can_traffic()
    start_driver()
    result = subprocess.run(['python3', str(Path(__file__).with_name('robot_ready.py')), '--check-only'],
                            capture_output=True, text=True, timeout=25)
    if result.returncode:
        stop('driver')
        raise RuntimeError('机械臂反馈检查失败：' + result.stdout + result.stderr + '\n' + tail('driver'))
    return json.loads(result.stdout)


def main():
    action = sys.argv[1] if len(sys.argv) > 1 else 'status'
    with (STATE / 'manager.lock').open('w') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        if action == 'develop':
            develop()
        elif action == 'live':
            live()
        elif action == 'stop':
            for name in ('bridge', 'rviz', 'driver', 'endpoint'):
                stop(name)
        elif action == 'rviz':
            if not read('driver'):
                raise RuntimeError('请先连接实机，再打开真实反馈 RViz')
            start('rviz', ['ros2', 'launch', 'agx_arm_description', 'display.launch.py',
                           'arm_type:=piper', 'effector_type:=agx_gripper',
                           'follow:=true', 'control:=false'])
        elif action == 'can':
            can_info()
        elif action == 'inspect':
            robot = inspect_robot()
            print(json.dumps(dict(status(), robot=robot), ensure_ascii=False))
            return
        elif action != 'status':
            raise ValueError('Unknown action')
        print(json.dumps(status(), ensure_ascii=False))


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print(json.dumps({'ok': False, 'error': str(error)}, ensure_ascii=False))
        sys.exit(1)
