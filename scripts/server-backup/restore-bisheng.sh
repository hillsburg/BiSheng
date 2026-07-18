#!/usr/bin/env bash
# 从 backup-bisheng.sh 产生的目录恢复 bisheng.db + uploads/
# 必须先停止 BiSheng 服务。详见运维指南。
set -euo pipefail

BISHENG_HOME="${BISHENG_HOME:-/opt/bisheng}"
BACKUP_DIR="${1:-}"

if [[ -z "${BACKUP_DIR}" || ! -d "${BACKUP_DIR}" ]]; then
  echo "用法: $0 /path/to/backup/YYYYMMDD-HHMMSS" >&2
  exit 1
fi

if [[ ! -f "${BACKUP_DIR}/bisheng.db" ]]; then
  echo "错误: ${BACKUP_DIR}/bisheng.db 不存在" >&2
  exit 1
fi

if systemctl is-active --quiet bisheng 2>/dev/null; then
  echo "错误: bisheng 服务仍在运行。请先: sudo systemctl stop bisheng" >&2
  exit 1
fi

TS="$(date +%Y%m%d-%H%M%S)"
SAFETY="${BISHENG_HOME}/pre-restore-${TS}"
mkdir -p "${SAFETY}"

if [[ -f "${BISHENG_HOME}/bisheng.db" ]]; then
  cp -a "${BISHENG_HOME}/bisheng.db" "${SAFETY}/"
  [[ -f "${BISHENG_HOME}/bisheng.db-wal" ]] && cp -a "${BISHENG_HOME}/bisheng.db-wal" "${SAFETY}/" || true
  [[ -f "${BISHENG_HOME}/bisheng.db-shm" ]] && cp -a "${BISHENG_HOME}/bisheng.db-shm" "${SAFETY}/" || true
fi

if [[ -d "${BISHENG_HOME}/uploads" ]]; then
  mv "${BISHENG_HOME}/uploads" "${SAFETY}/uploads"
fi

cp -a "${BACKUP_DIR}/bisheng.db" "${BISHENG_HOME}/bisheng.db"
# 恢复后去掉可能残留的 WAL，避免旧 WAL 与新主库混用
rm -f "${BISHENG_HOME}/bisheng.db-wal" "${BISHENG_HOME}/bisheng.db-shm"

if [[ -f "${BACKUP_DIR}/uploads.tar.gz" ]]; then
  tar -C "${BISHENG_HOME}" -xzf "${BACKUP_DIR}/uploads.tar.gz"
fi

if [[ -f "${BACKUP_DIR}/data-protection-keys.tar.gz" ]]; then
  tar -C "${BISHENG_HOME}" -xzf "${BACKUP_DIR}/data-protection-keys.tar.gz"
fi

chown -R bisheng:bisheng "${BISHENG_HOME}/bisheng.db" "${BISHENG_HOME}/uploads" 2>/dev/null || true

echo "恢复完成。当前库已替换；原文件在: ${SAFETY}"
echo "请执行: sudo systemctl start bisheng && sudo systemctl status bisheng"
