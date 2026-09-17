#!/bin/bash
# Invoked explicitly as WSL root by the USB-CAN button; no persistent sudo rule.
set -euo pipefail
# USB attachment returns before gs_usb finishes creating the network interface.
for attempt in {1..40}; do
  test -d /sys/class/net/can0 && break
  sleep 0.2
done
test -d /sys/class/net/can0 || { echo '挂载后仍未找到 can0，请检查 USB-CAN 驱动'; exit 1; }
# Already configured: leave a working bus untouched, including while a driver runs.
if ip -details -json link show can0 | python3 -c 'import json,sys; i=json.load(sys.stdin)[0]; d=i.get("linkinfo",{}).get("info_data",{}); sys.exit(0 if "UP" in i.get("flags",[]) and d.get("bittiming",{}).get("bitrate")==1000000 and d.get("state") not in ("BUS-OFF","STOPPED","ERROR-PASSIVE") else 1)'; then
  echo 'can0 已就绪，保留现有配置'
  exit 0
fi
# Refuse to reconfigure CAN underneath an existing robot controller.
if pgrep -f '[a]gx_arm_ctrl_single' >/dev/null; then
  echo '驱动仍在运行，请先停止服务'; exit 1
fi
ip link set can0 down
ip link set can0 type can bitrate 1000000
ip link set can0 txqueuelen 1000
ip link set can0 up
ip -details link show can0
