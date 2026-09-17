#!/usr/bin/env bash
set -e
export RMW_IMPLEMENTATION=rmw_cyclonedds_cpp
export ROS_LOCALHOST_ONLY=1
export ROS_DOMAIN_ID=0
export PYTHONUNBUFFERED=1
surf_backend="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
export SURF_WS="$(cd "$surf_backend/.." && pwd)"
source /opt/ros/humble/setup.bash
source "$SURF_WS/install/setup.bash"
exec python3 "$surf_backend/manager.py" "$@"
